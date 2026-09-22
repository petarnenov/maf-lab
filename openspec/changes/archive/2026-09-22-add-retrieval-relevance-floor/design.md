# Design

## Context

See proposal.md — Why. Four facts from the code shape everything below.

**Fusion happens in Qdrant, not here.** `TenantScopedSearch.HybridAsync` sends the two branches as
`PrefetchQuery` objects and asks the store to fuse them:

```csharp
return await client.QueryAsync(_collection, query: fusion, prefetch: prefetch, filter: filter,
    limit: (ulong)request.Limit, payloadSelector: true, cancellationToken: ct);
```

The response carries the fused score only. Per-branch scores never come back to us, so a floor
cannot be applied here after the fact — it has to go where the branches still exist.

**Qdrant supports exactly that.** Verified against Qdrant.Client 1.19.0, the pinned version:

```
PrefetchQuery score props: ScoreThreshold, HasScoreThreshold
QueryAsync(Query query, IReadOnlyList`1 prefetch, Nullable`1 scoreThreshold)
```

Each prefetch branch takes its own threshold, and the single-branch dense and sparse queries take
one on the call. Nothing needs to be built; the capability is in the client already.

**The whole zero-result path exists and is unreachable.** `SearchAsync`'s `top.Count == 0` hint,
`ChatTurnRunner.cs:367`'s `ZeroResults`, `TurnSignals.Compute`'s `zero_retrieval_results`, and the
review queue behind it. There is also precedent for returning empty deliberately: the
`IdentifierOnly()` early return already answers a query like "4417" with no results and a hint.

**The eval suite cannot currently judge this.** `RetrievalSuite` calls `RankAsync` with k=20 over
49 cases, all of them answerable, and scores recall@5, recall@20 and MRR. Nothing in it would
notice a floor doing its job; everything in it would notice a floor doing harm. That asymmetry is
why the dataset has to grow before the number can be chosen.

## Goals / Non-Goals

**Goals:**

- Make the existing zero-result path reachable, so an unanswerable question is flagged rather than
  silently dressed in citations.
- Keep the operator's view strictly richer than the model's: the floor removes candidates from the
  answer, never from diagnostics.
- Choose the floor from measurement, and make it possible to measure.
- Leave the door open: the floor can be turned off and the system behaves exactly as today.

**Non-Goals:**

- Changing how sources are derived. `Summarise` making a source of every returned row is correct
  once the returned rows are worth citing; the defect is the rows, not the mapping.
- A topicality gate in the intent classifier. That was considered and rejected (see Decisions).
- Any change to rerank. The floor runs before rerank; a reranker that receives nothing returns
  nothing, which is the behaviour we want.
- Tuning `PrefetchMultiplier`, `MinPrefetch` or the candidate limits.

## Decisions

### The floor goes on the prefetch branches

Two thresholds, dense and sparse, set on each `PrefetchQuery` in `HybridAsync` and on the
`QueryAsync` call in the dense-only and sparse-only modes. A branch with nothing above its floor
contributes an empty list to fusion; when both are empty, Qdrant returns no points and
`QueryAsync` returns an empty list, which is precisely the state the rest of the system has been
waiting for.

Alternative considered: filter `candidates` in `DocumentSearchService` after the fact. Impossible
for hybrid — the fused RRF score is all we get back, and it carries no information about distance
to the query. RRF is `1/(k + rank)` summed across branches; a document ranked first in a branch
scores the same whether it was a perfect match or the least bad of a bad lot. A floor there would
be a number that cannot be wrong because it cannot mean anything.

Alternative considered: one floor, applied to whichever branch. Rejected — dense cosine similarity
and BM25 are unrelated scales. In the observed traces dense scores sit in 0.46–0.70 while sparse
BM25 runs 1.4–6.2. One number cannot serve both.

### The diagnostic branch queries do not get the floor

`DocumentSearchService` already re-runs dense-only and sparse-only queries when `TraceBranches` is
on, to populate the monitor's per-branch lists. Those re-runs keep running unfiltered, and the
diagnostics record the floors separately so the view can mark which candidates fell below them.

This is the one place where the operator's path and the model's path deliberately diverge. It
costs an unfiltered query that was already being made, and it buys the only thing that makes a
floor debuggable: seeing that the search found something at 0.47 when the floor was 0.55 is what
tells you whether the floor is wrong or the corpus is missing a document.

Alternative considered: filter everywhere and record only a count of what was dropped. Rejected —
a count tells you a floor fired, not whether it should have.

### Config, with off as a real setting

Two nullable values on `RetrievalOptions`, next to `PrefetchMultiplier` and `MinPrefetch`. Null
means no floor, and null is what a deployment gets until the calibration produces a number. That
keeps this change safe to merge before the number exists, and it makes "turn it off" a supported
state rather than an emergency edit.

The eval variants each carry their own `SearchSettings`, which is how the suite compares
configurations today. The floors belong there too, so a calibration run can sweep values without
touching configuration.

### What the number has to clear

The calibration is the work, not the code. Present evidence, from four traces read in the running
system:

| Query | Domain | Dense top scores |
|---|---|---|
| "What is JWE?" | off | 0.471, 0.467, 0.467, 0.466 |
| "I want you to explain JWE as if to a 6-year-old!" | off | 0.534, 0.528, 0.522, 0.520 |
| "how to run" | on | 0.662, 0.659, 0.652, 0.651 |
| "I want to reset the fee on account A-1042…" | on | 0.702, 0.680, 0.676, 0.676 |

A gap between roughly 0.53 and 0.65 is visible, which suggests a dense floor somewhere near 0.55–0.60.
Four traces are an observation, not a calibration: the 49-case suite has to confirm that recall@5
(baseline 0.6871 for hybrid), recall@20 (0.9116) and MRR (0.6347) survive, and the off-domain
cases have to confirm the floor actually silences them.

The sparse floor is the harder half and has less evidence behind it. BM25 scores scale with query
length and term rarity, so a floor that suits a six-word question may reject a two-word one. If the
sweep shows no sparse value that behaves across query lengths, the honest outcome is a dense floor
only, recorded as such — the JWE case is cut by the dense floor regardless, because its sparse
branch was already empty.

### If no value works, that is the result

The sweep can fail: there may be no pair that both preserves recall and silences the off-domain
cases. If so, this change ships the mechanism with the floors null — off, and therefore a no-op —
and reports what the sweep showed. A floor chosen to make a table look right, without a separation
in the data to support it, would trade a visible defect for an invisible one: on-domain questions
quietly answered with "no matching documentation".

## Risks / Trade-offs

- **A floor turns a bad answer into no answer.** Today an off-domain question gets a truthful
  "we don't have that" plus bad citations; a floor set too high would do the same to a real
  question. → The floor is calibrated against recall on 49 answerable cases, gated by the existing
  baseline thresholds, and off by default until a number earns its place.
- **Qdrant applies the threshold inside the branch, before fusion.** A document that is a weak
  dense match and a strong sparse match — which RRF exists to rescue — is dropped from the dense
  branch and survives only through sparse. → This is the intended trade and the reason the floors
  are independent and low: each is meant to remove what is *far*, not to pick winners. The
  recall@20 metric is where this would show up if the floors are set too aggressively.
- **Off-domain cases are written by whoever writes them.** A dataset of off-domain questions
  chosen to be obviously off-domain proves little. → The first entries come from real traces
  already in the store, including the reported JWE turns, not from imagination.
- **The eval suite measures `RankAsync`, the tool returns through `SearchAsync`.** The floor must
  sit below both or the suite measures something the users do not get. → Placing it in
  `TenantScopedSearch` puts it under both paths, which is also where the tenant filter lives, and
  that is the project's existing rule about there being one query path.
- **Two unarchived changes now touch the same capabilities.** `fix-retrieval-tab-null-idf` has
  pending deltas on `retrieval-tool`'s "Retrieval diagnostics on request" and `web-ui`'s "Monitor
  views". → This change adds new requirements instead of modifying those two, so the deltas do not
  collide at archive time. Archiving the earlier change first would allow tidier placement, and is
  worth doing before this one is archived.

## Migration Plan

Merge with both floors null: the mechanism is in place and inert, and every existing behaviour is
byte-identical. Calibrate, then set the values in configuration and accept a new eval baseline.
Rolling back is setting them to null again — no data, no schema, no stored traces are affected.

## Calibration result

The sweep ran against the indexed corpus through `make eval SUITE=retrieval`, 47 s per run.

**Dense floor, sparse floor off** — 0.45 to 0.70. Off-domain silence rises from 0 to 0.5 and then stops:
the remaining three off-domain questions reach the tool through the sparse branch, not the dense one.
The `dense` variant alone reaches silence 1.0 at 0.65, which is what identifies the sparse branch as the leak.

**0.65 with no sparse floor**, three runs, identical every time, and every metric at or above the accepted
baseline:

| metric | baseline | 0.65 | Δ |
|---|---|---|---|
| recall@5 | 0.6871 | 0.697 | +0.010 |
| recall@20 | 0.9116 | 0.932 | +0.020 |
| mrr | 0.6347 | 0.648 | +0.013 |
| recall@5:bg | 0.6806 | 0.701 | +0.020 |
| recall@5:en | 0.6933 | 0.693 | — |
| offDomainSilence | 0 | 0.5 | +0.5 |

Dropping candidates that are merely far improves recall@20 rather than harming it: the room they took in the
top twenty goes to sparse candidates that deserve it more.

**Sparse floor** — 1 to 20, with the dense floor fixed. Nothing below 6 moves silence at all, because the
off-domain queries score *higher* on BM25 than the on-domain ones: their terms ("want", "explain") are rarer in
this corpus than billing vocabulary, and BM25 rewards rarity. Silence reaches 1.0 at 9, and costs
`recall@5:bg` 0.042 — outside the harness's 0.02 regression tolerance, and precisely the failure the
per-language metric exists to expose. Three runs at 0.65/9 gave `recall@5:bg` 0.639 every time against an
unfloored mean of 0.681.

So the sparse floor was left null. A BM25 score is comparable between documents within one query's ranking and
not between queries, which makes a fixed floor on it ill-posed rather than merely untuned. Silencing the
remaining half needs something that knows what the query is about, not a higher number.

One incidental finding worth keeping: floored runs are deterministic where unfloored ones are not. Unfloored
recall@5 varied 0.677–0.697 across three runs, floored did not vary at all. The variance came from marginal far
candidates moving in and out of the top five as the translation model worded a query slightly differently; with
them below the floor, the ranking stops flickering.
