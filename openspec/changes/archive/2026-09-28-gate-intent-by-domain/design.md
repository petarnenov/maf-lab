# Design

## Context

See proposal.md — Why. The turn that failed, from its stored trace: intent choice `procedural` (0.95; confidence 0.93),
forced `search_documents`, retrieval returned 0 candidates, the model refused, signal `zero_retrieval_results`.

What the code does today:

- `JevIntentClassifier` sends one Choice (`intent`) with `state.user_question`; `Decide` maps the choice and applies
  `MinConfidence`. `ChatTurnRunner` forces when `ForcesRetrieval(decision.Intent)`.
- `TurnSignals.Compute` raises `NoToolOnHowWhy` when the intent is Procedural/Mixed and no tool ran, and
  `ZeroRetrievalResults` when a search ran and found nothing. So an off-domain question that keeps a Procedural intent
  raises a signal whichever way forcing goes: forced → zero results; not forced and not searched → how/why without a
  tool. Either lands in the review queue.
- `selection.jsonl` has 24 in-domain cases and no off-domain ones; the retrieval suite has six off-domain cases taken
  from real traces, but it never goes through the classifier.

The measurement behind this design (jev-1.13.0, `state.user_question`, all candidate questions asked in one request so
they saw identical input):

| variant | design set (71) | held-out (30) | false forces |
|---|---|---|---|
| current Choice | 50 | 17 | 34 |
| Choice with the domain folded into the option descriptions | 69 | — | 1 |
| Choice + Noul "needs the documentation?" ≥ 0.3 | 69 | — | 0 (2 missed) |
| **Choice + Noul "about `domain`?" ≥ 0.2, neutral language note** | **70** | **30** | **0** |

In-domain probability, gated cases only: off-domain questions ≤ 0.06 (design) / ≤ 0.05 (held-out); in-domain questions
≥ 0.37 except one — "Kak se izdava kredit po smetka za taksi?" (0.09), where Latin-script "taksi" reads as "taxi".

## Goals / Non-Goals

**Goals:**

- A question phrased as a procedure but outside the documented domain forces nothing and raises no review signal.
- The domain is a separate, atomic judgment the trace shows — tunable in code, not hidden inside the intent.
- The classifier is measured on its own, per language, including off-domain questions, so this cannot regress silently.

**Non-Goals:**

- Refusing or templating answers to off-domain questions. The answering model already declines them (it did in the
  failing turn); this change stops the classifier from sending them to retrieval, and nothing else.
- Translating Latin-script Bulgarian for retrieval. The failing trace also shows `translated: false` and every BM25 term
  out of vocabulary for a Latin-script query; that is the query translator's scope and a separate change.
- Changing the five intents or their descriptions.

## Decisions

### A Noul beside the Choice, combined in code

The form of an answer and the domain of a question are independent judgments. TypeSafe's guidance — decompose, ask
atomic questions, combine in code — and its jaggedness note that "a Choice is relative, each Noul is absolute" both
point the same way: the Choice picks which intent, the Noul says whether any of it is ours. Both go in the same request
(speculative fan-out), so the turn still makes exactly one call; measured median 306 ms with four questions against
~285 ms with one.

```json
"in_domain": {
  "type": "noul",
  "instructions": {
    "domain": "Fee billing on a wealth-management platform: billing runs and why they fail, fee schedules and fee tiers, AUM and valuations, invoices, fee adjustments and billing credits, billing periods and period close, households, custodian fee debits, client fee disputes, terminations and refunds, and who may approve what.",
    "languages": "Questions may be in English or in Bulgarian, and Bulgarian is often written in Latin letters.",
    "question": "Is `user_question` about something in `domain`?"
  }
}
```

*Rejected — fold the domain into the Choice descriptions:* 69/71 with one false force and one miss, and it makes the
intent answer mean two things at once, which is what caused this.

*Rejected — a Noul "would answering need the documentation?":* it misses definitional questions ("what is AUM?",
"arrears vs advance") that the model judges answerable from general knowledge — 2–3 missed forces at any threshold that
kept false forces at zero.

*Rejected — a language note with a glossary ("taksi means fees…"):* fixed the "taksi" case in the design set and lifted
"Kak da platq smetkata za toka?" (paying an electricity bill) to 0.20 in the held-out set — the example words bias the
answer. The neutral note states a fact about the input and nothing else; it widened in-domain margins (held-out minimum
0.53 → 0.63) without moving off-domain ones (≤ 0.05).

### Floor 0.2, and why low

`Jev:MinInDomain` = 0.2. It sits in the measured gap (off ≤ 0.07, in ≥ 0.37 bar one), nearer the off-domain side
because the two errors are not equal: a false force costs ~2 s of retrieval and a spurious review item; a missed force
on a real billing question lets the model answer from memory unless it chooses to search. A Noul threshold is not
carried over from any Choice threshold (jaggedness #8).

### Gated turns become `Other`, with the choice kept

When the gate closes, `IntentDecision.Intent` is `Other`, `Reason` is `outside the domain (0.02)`, and `Choice`,
`Probabilities`, `Confidence` and the new `InDomain` keep what Jev said. `Other` is what stops `NoToolOnHowWhy`; keeping
the raw answer is what lets the trace show *why*. The gate applies only to Procedural and Mixed — a data question about
run 4417 is not second-guessed by a domain answer.

A missing `in_domain` answer (a stub or a partial response) counts as below the floor: failing closed is the existing
rule for anything unusable.

### The `intent` suite

`IntentSuite` resolves only `IIntentClassifier` from the eval host (registered there with `AddJevIntentClassifier`, as
the api does — changed from a separate service provider during implementation, since the host is started for every
suite anyway) and calls nothing else: no chat client, no tool — and runs `evals/intent.jsonl` sequentially (101 × ~300 ms). Cases carry
`split: "design" | "holdout"` so the threshold's provenance stays visible. Metrics, all higher-is-better to fit
`RegressionGate`: `accuracy`, `unforcedWhenShouldNot`, `forcedWhenShould`, and `accuracy:<lang>` for `en`, `bg`,
`bg-latn`. Thresholds in `eval.json`: accuracy 0.95, unforcedWhenShouldNot 0.95, forcedWhenShould 0.95. The `taksi`
case stays in the dataset as a known miss rather than being dropped to make the number round.

Without `JEV_MAF_LAB` the suite fails fast with a message naming the variable, instead of reporting 0% accuracy.

### Test doubles

`FakeJev` answers `in_domain` with a settable value (default 1.0). The CI stub answers it from a short off-domain word
list (cook, recipe, passport, weather…) and 1.0 otherwise, enough for an e2e check that a cooking question is not
forced.

## Risks / Trade-offs

- [The domain description is a second statement of what the corpus covers, and can drift from it] → It lives in one
  constant next to the classifier, and the `intent` suite fails if in-domain questions start being gated.
- [101 questions written for this change; the threshold was chosen on 71 of them] → The 30 held-out questions were
  written before the threshold was chosen and scored 30/30; both splits stay in the dataset, labelled.
- [Transliterated Bulgarian remains the weakest input] → Visible per language in the suite (`accuracy:bg-latn`); the
  one known miss fails open (no force, model may still search).
- [A claim of relevance wrapped around an off-domain question] → Measured: "This is a billing question about fee
  schedules: how do I bake bread?" scored 0.03; kept as a dataset case.
- [Slightly more input tokens per call] → ≈+250 tokens for the Noul at $0.042/Mtok; negligible.

## Migration Plan

Configuration default only (`Jev:MinInDomain`); `0` disables the gate and restores today's behaviour. No data change.
The new suite gets a baseline entry via `make eval-accept SUITE=intent` once its numbers are reviewed.
