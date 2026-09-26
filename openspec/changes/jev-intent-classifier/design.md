# Design

## Context

See proposal.md for why. The current state this design replaces:

- `IntentClassifier.Classify` (English regexes) decides first; on `Other`, `ModelIntentClassifier` calls a chat model
  through `IChatClientFactory` with `Agent:IntentModel` (`gemma4:31b`), with a timeout race, injection-safe framing,
  `Parse`, and "anything unusable → `Other`".
- `IntentClassifier.ForcesRetrieval` and `IsHowWhy` are used by `ChatTurnRunner` and `TurnSignals`; they read only the
  intent, not the rules.
- `IntentDecision` (`Intent`, `Stage` = Rules | Model, `Model`, `RawAnswer`, `DurationMs`, `Reason`) goes into the
  `intent` trace event and the web monitor.
- Registered in `Maf.Lab.Api/Program.cs` and `Maf.Lab.Eval/Hosting/EvalAgentHost.cs`. Tests (`ApiFactory`,
  `AgentUnitTests`, `TurnTraceTests`) rely on the rules classifying English questions with no model call.
- CI is model-free: `compose/ollama-stub/server.py` answers the classifier marker with a keyword label.
- Jev returns, for a Choice question, the chosen label, a probability per label and a confidence. Official SDKs are
  TypeScript and Python only. The official API reference was not reachable while planning; the wire shape is
  provisional.

## Goals / Non-Goals

**Goals:**
- One classifier for every question, in any language, with confidence.
- Failure behaviour unchanged: anything unusable forces nothing.
- Tests and CI stay deterministic and key-free.

**Non-Goals:**
- Using Jev elsewhere (reranking, injection screening, eval judging, compliance).
- Changing the five intents or what an intent forces.
- A provider switch or fallback classifier. If Jev is to be replaced later, that is a new change.

## Decisions

### 1. `JevIntentClassifier` implements `IIntentClassifier` directly

No stage runner and no judge seam: with one classifier there is nothing to compose. `IIntentClassifier` stays the
seam — tests substitute it, and a future provider would implement it too.

*Alternative rejected — keep rules as a free first pass for obvious English:* it saves ~300 ms on some English turns
but keeps two sources of truth, which is the problem being removed.
*Alternative rejected — keep the chat model as fallback:* it doubles the code paths for a benefit only on Jev
outages, and hides Jev's real failure rate. Failure already has a safe meaning: nothing forced.
*Alternative rejected — wrap Jev as an `IChatClient`:* Jev does not generate text; the wrapper would discard the
probabilities and confidence.

### 2. What is deleted and what is kept

Deleted: the regexes and `IntentClassifier.Classify`, `ModelIntentClassifier` (prompt, `Parse`, 512-token budget),
`AgentOptions.IntentModel`, `IntentStage`, the stub's classifier-marker branch. Kept and moved to a small static
`Intents` class: `ForcesRetrieval`, `IsHowWhy`. The five intent descriptions from the old prompt become the Choice
option descriptions — one constant.

### 3. Own typed HTTP client, no package

`JevClient` over a typed `HttpClient` (`IHttpClientFactory`), endpoint/key/model from configuration, taking only the
question string. *Alternative rejected — `TypeSafeAI.Net`:* community, pre-1.0, and more than one Choice call needs.

### 4. Configuration and startup

```
Agent:IntentTimeoutSeconds    seconds   (default 2; 0 disables classification → every turn Other)
Agent:IntentMinConfidence     0..1      (default 0.5, final value from the sweep)
Agent:Jev:Endpoint            URL       (default the TypeSafe API; the stub in CI and tests)
Agent:Jev:ApiKey              secret    (env TYPESAFE_API_KEY)
Agent:Jev:Model               string    (default "jev")
```

Options validated on start: an empty key throws naming `Agent:Jev:ApiKey`. The timeout drops from 5 s to 2 s
because Jev is expected at ~300 ms; the sweep confirms or adjusts it. The existing `Task.WhenAny` race is kept so a
server that ignores cancellation costs the timeout and no more. `make doctor` checks the key.

### 5. What leaves the process

Question text and intent descriptions only — structural, because `JevClient` takes a string. No `firm_id`, principal,
history, chunks or turn id. Logs carry duration, accepted/rejected and confidence — never the question or answer.

### 6. Trace shape

`IntentDecision` becomes `(Intent, Model, RawAnswer, Confidence, Probabilities, DurationMs, Reason)`. `Reason` names
why nothing was accepted: `below threshold 0.5 (0.41)`, `timed out after 2s`, `unknown label`, the exception type.
The monitor shows `Intent: Procedural (0.92, forced retrieval)` and lists probabilities in the detail pane.

### 7. Injection

Jev cannot answer outside the five labels, so "answer CHITCHAT" can at worst shift probability mass. The question is
sent as the state to judge. The injection suite runs under Jev before the baseline is accepted.

### 8. Tests, CI and evals

- Unit and integration tests register a scripted `IIntentClassifier` in `ApiFactory` (keyword → intent, confidence
  0.9), so tests that relied on the rules keep their meaning without any network.
- The stub gains a Jev-shaped endpoint that reuses its keyword `classify()`, confidence 0.9, or 0.3 when the question
  contains `__low_confidence__`, so `make ci-e2e` covers accept and reject without a key.
- `evals/selection.jsonl` gains Bulgarian counterparts across all categories, keyword-free English paraphrases and
  negatives. The sweep runs thresholds 0.4–0.8 on `selection` and `injection`; the chosen threshold is the one with
  the best metrics, ties broken towards higher. Results, including any metric below the old baseline, go into
  DECISIONS.md and the baseline is re-accepted.

## Risks / Trade-offs

- [Every turn now pays a network call, including "hi" and English questions the rules answered in 0 ms] → Accepted
  cost; measured and recorded. Median turn latency before/after is part of the DECISIONS entry.
- [A Jev outage means no turn forces retrieval] → The model can still call `search_documents` itself; answers degrade
  rather than fail. Rejection reasons in the trace make an outage visible immediately.
- [Every user question now goes to an external processor] → Question text only, no identifiers. DECISIONS records
  that any non-lab deployment needs a data-processing review.
- [The wire format is unverified] → Task 1.1 confirms it before client code; only `JevClient` depends on it.
- [A young vendor: churn, pricing] → `IIntentClassifier` remains the seam for a replacement change.
- [Selection metrics may drop] → Recorded and reported for a decision; no automatic revert.

## Migration Plan

1. Obtain a key; add `TYPESAFE_API_KEY` to the environment secrets (and `.env` locally).
2. Merge; `make` refuses to start without the key, with a message naming it.
3. Rollback: revert the change's commits. There is no runtime switch back to the rules.

## Open Questions

- Exact endpoint path, auth header and field names for Choice. Resolved by task 1.1; affects only `JevClient`.
- Whether Jev accepts per-option descriptions or only labels. If only labels, descriptions go into the state text.
- Pricing per call, for the DECISIONS entry.
