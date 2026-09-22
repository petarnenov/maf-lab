# Design

## Context

See proposal.md — Why. Two facts from the code make this small.

**`Compute` already has what it needs.** `TurnSignals.Compute(intent, toolCalls, zeroResults,
answerChars, sourceCount, longAnswerChars)` is handed the turn's own `sourceCount`, and
`LongAnswerWithoutSources` already reads it. Only the zero-retrieval rule looks at the per-call
flag instead.

**Sources come from one tool.** `ChatTurnRunner.Summarise` adds to `state.Sources` only in the
`search_documents` case; every other tool returns a summary and no sources. So a turn's
`sourceCount > 0` already means a search returned rows, and `sourceCount == 0` plus "a search ran"
is exactly the condition the signal's name promises.

## Goals / Non-Goals

**Goals:**

- The signal means what it is called, so the review queue holds turns a reviewer can act on.
- A turn that recovered stays out of the queue.
- A turn that searched and ended empty stays in it, as today.

**Non-Goals:**

- Changing any other signal. `no_tool_on_how_why`, `rephrased`, `negative_feedback` and
  `long_answer_without_sources` are untouched.
- Changing what the relevance floor does, or when a search returns nothing.
- Fixing the query translation. The failure that produced the observed turn is real and worth
  fixing; it is a separate change with its own evidence to gather.

## Decisions

### The flag records that a search ran, not that one failed

`state.ZeroResults` becomes `state.Searched`, set when a `search_documents` call completes without
error — regardless of what it returned. `Compute` then raises the signal on
`searched && sourceCount == 0`.

This is the smallest change that makes the name true, and it removes a latch: the old flag could
only ever go from false to true, so a later success could not undo it. The new one is a fact about
the turn that cannot be contradicted by anything later in it.

Alternative considered: keep the per-call flag and add `&& sourceCount == 0` at the signal site.
That works, but leaves a field called `ZeroResults` that no longer means zero results, which is how
this defect started.

Alternative considered: count empty searches and raise the signal above a threshold. Rejected —
it invents a number to answer a question that has a definite answer.

### The unusable translation does not get a signal of its own

It is tempting: a failed translation is a real defect, it silently degrades every non-English
question, and it is exactly the sort of production fact worth surfacing. It is still rejected, for
three reasons that hold together.

The review queue is not a defect tracker. Its form appends a labelled row to an eval dataset, and a
turn that answered correctly from sources it cited gives a reviewer nothing to label. A signal that
routes such turns there makes the queue worse at its one job, which is the defect being fixed here
— adding a second signal that does the same thing would undo the change while making it.

The harmful case is already covered. A translation failure hurts when the turn cannot recover; that
turn ends with no sources and carries the corrected signal. When the turn does recover, nothing was
harmed except latency.

And the fact is not lost. The trace records `translationNote`, and the Retrieval tab already
renders it — "searched as written: …" — beside the candidates the floor dropped and the count of
what was returned. Someone looking at that turn sees exactly what happened.

If a *rate* of translation failures is wanted — and it may well be, since one turn cannot say
whether this is rare or constant — that is a counter in `LabTelemetry.Instruments`, next to the
retrieval-stage histogram, not a row in a labelling queue. Noted as a candidate; not built here.

## Risks / Trade-offs

- **A turn whose every search failed but which answered from memory keeps the signal.** That is
  correct and worth stating: `searched && sourceCount == 0` is true there, and an answer with no
  documentation behind it is precisely what a reviewer should see.
- **The signal fires less often than it did today.** That is the point, but it means the queue will
  look quieter, and a quiet queue can read as a broken one. → The existing tests pin both
  directions, and `FeedbackApiTests` already asserts the signal appears for a turn that searched and
  got nothing.
- **A future tool that returns sources would change the meaning of `sourceCount`.** → Only
  `search_documents` contributes sources today, and `Summarise` is the single place that decides;
  a new source-producing tool would have to pass through it.

## Migration Plan

None. No stored data is read or written differently; signals are computed per turn as it ends. Turns
already flagged in the queue keep the signal they were stored with — the change affects turns from
here on, and the queue is a working list, not a record.

## Verified against the turn that started this

Same question, same stack, after the change:

```
'Каква е процедурата при липсваща фий схема?'  searches=[0, 20, 20]  sources=9  signals=[]
```

The translation still fails, the first search still returns nothing, and the turn still answers —
but it no longer asks for a reviewer. Asked cold in a new conversation, the off-domain question
still does:

```
'What is JWE?'  searches=[0]  sources=0  signals=['zero_retrieval_results']
```

Both appear in `/admin/feedback` as expected: the unanswerable one flagged, the recovered one
absent. The rows already in the queue from before the change keep the signal they were stored
with, which is what the migration note says and why there is nothing to migrate.

**Noticed while verifying, for whoever takes the eval gates next.** `retrieval` failed its
regression gate on one run and passed on the next, on `recall@5:bg` swinging 0.034. It already
carries a per-suite tolerance of 0.03 — the default is 0.02 — and that was not enough, because the
measured swing of that one metric is about 0.041 while the suite's other metrics barely move. So a
per-suite number may be the wrong granularity: the noise lives in particular metrics, not in whole
suites.
