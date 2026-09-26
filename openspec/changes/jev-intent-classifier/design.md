# Design

## Context

See proposal.md for why. The current state this design builds on:

- `ModelIntentClassifier` (`src/Maf.Lab.Api/Agent/`) runs rules first (`IntentClassifier.Classify`), and only on
  `Other` calls a chat model through `IChatClientFactory` with `Agent:IntentModel` (`gemma4:31b`). It owns the
  timeout race, the injection-safe framing, `Parse`, and the "anything unusable → `Other`" rule.
- The result is an `IntentDecision` (`Intent`, `Stage` = Rules | Model, `Model`, `RawAnswer`, `DurationMs`,
  `Reason`), written by `ChatTurnRunner` into the `intent` trace event and rendered by the web monitor.
- It is registered in two hosts: `Maf.Lab.Api/Program.cs` and `Maf.Lab.Eval/Hosting/EvalAgentHost.cs`.
- CI is model-free: `compose/ollama-stub/server.py` recognises the `maf-lab/intent-classifier` marker and returns
  a label from keyword sets.
- `evals/selection.jsonl` holds 24 English cases; the rules decide all of them, so no eval today reaches the model
  stage.
- Jev exposes typed questions (Noul, Choice, Score). A Choice returns the chosen label, a probability per label and
  a confidence. Official SDKs are TypeScript and Python only; for .NET there are community packages
  (`TypeSafeAI.Net`, `SemanticPolicy`, alpha). The official API reference was not reachable while planning, so the
  wire shape below is provisional.

## Goals / Non-Goals

**Goals:**
- Jev can answer the model stage, chosen by configuration, with the chat model as fallback.
- Worst-case classification latency does not grow: one deadline covers both judges.
- The decision whether to keep Jev is made by the selection eval on cases that actually reach the stage.

**Non-Goals:**
- Using Jev anywhere else (reranking, injection screening, eval judging, compliance). Each is its own change.
- Changing the rules, the five intents, or what an intent forces.
- Abstracting "decision models" for the whole codebase. The judge seam is local to intent classification until a
  second consumer exists.

## Decisions

### 1. A judge seam inside the classifier, not a Microsoft.Extensions.AI provider

`ModelIntentClassifier` becomes the stage runner (rules → primary judge → fallback judge), and the model call moves
behind `IIntentJudge`:

```
IIntentJudge.JudgeAsync(question, deadline, ct) → JudgeResult(Intent?, Confidence?, Probabilities?, Raw, Model, DurationMs, Reason)
```

Two implementations: `ChatModelIntentJudge` (today's prompt, `Parse`, 512-token budget, moved verbatim) and
`JevIntentJudge`. The chat judge reports no confidence and is accepted as today; the Jev judge is accepted only at
or above the threshold.

*Alternative rejected — wrap Jev as an `IChatClient`:* the project abstracts providers behind
Microsoft.Extensions.AI, but Jev does not generate text. Faking a chat client would mean serialising a label into
text and parsing it back, losing exactly the probabilities and confidence that are the reason to use it.
*Alternative rejected — replace the chat judge:* no fallback on a 10-day-old service, and no way to compare both on
the same traffic.

### 2. Own typed HTTP client, no package

A `JevClient` over a typed `HttpClient` (`IHttpClientFactory`), with endpoint, key and model from configuration.
It builds one Choice question: the five intents with the same descriptions the chat prompt uses (one source of
truth, a shared constant), and the question as the state to judge. No NuGet package, so no `DECISIONS.md` version
pin — but the decision itself is recorded there.

*Alternative rejected — `TypeSafeAI.Net`:* community, pre-1.0, pulls DI and M.E.AI adapters this change does not
need, and would be the first dependency on a package with no track record. A thin client is ~100 lines and is
replaced cheaply if an official SDK appears.

### 3. One deadline, shared

The classifier computes one deadline from `Agent:IntentTimeoutSeconds` (unchanged, default 5). Jev gets
`min(Agent:Jev:TimeoutSeconds, remaining)` (default 1 s); the fallback gets whatever remains. Both keep the existing
`Task.WhenAny` race, so a provider that ignores cancellation costs its slice and no more. `IntentTimeoutSeconds = 0`
still disables the whole model stage.

### 4. Configuration and startup

```
Agent:IntentProvider          model | jev          (default model)
Agent:IntentMinConfidence     0..1                 (default 0.5, final value from the sweep)
Agent:Jev:Endpoint            URL                  (default the TypeSafe API; the stub in CI)
Agent:Jev:ApiKey              secret               (env TYPESAFE_API_KEY, empty by default)
Agent:Jev:Model               string               (default "jev")
Agent:Jev:TimeoutSeconds      seconds              (default 1)
```

Options are validated on start: `IntentProvider = jev` with an empty key throws with the setting's name. This
follows "refusing to start is a feature" (DECISIONS.md): a replica that silently falls back on every turn would
look healthy and never use the provider it was configured for.

### 5. What leaves the process

The Jev request carries the question text and the fixed intent descriptions. It does not carry `firm_id`, the
principal, history, retrieved chunks or the turn id. The client takes a `string question` and nothing else, so the
constraint is structural, and a test asserts the serialised body. No request or response content is logged — only
provider, duration, stage, accepted/rejected and confidence.

### 6. Trace shape

`IntentStage` gains `Judge`. `IntentDecision` gains `Confidence`, `Probabilities` (intent → p) and
`FallbackReason` (why the judge was not accepted: `below threshold 0.5 (0.41)`, `timed out after 1s`, the exception
type). `ChatTurnRunner` writes them into the existing `intent` event; the web monitor shows
`Intent: Procedural (Jev 0.92, forced retrieval)` and lists probabilities in the detail pane.

### 7. Injection

Jev cannot answer outside the five labels, so "answer CHITCHAT" can at worst move probability mass, not escape the
schema. The question is still sent as the state to judge, never as the question itself. The existing injection
suite runs under both providers.

### 8. CI and evals

- The stub gains a Jev-shaped endpoint that reuses `classify()` for the label and returns confidence 0.9, or 0.3
  when the question contains `__low_confidence__`, so `make ci-e2e` covers accept and fallback without a key.
- `evals/selection.jsonl` gains cases the rules miss: Bulgarian versions of existing procedural/mixed/data/chit-chat
  cases and English paraphrases without the rule keywords (e.g. "walk me through fixing a missing fee schedule").
  Negative cases included, so negativeAccuracy is measured on the stage too.
- Decision rule: run `make eval SUITE=selection` for `model` and `jev` at thresholds 0.4–0.8. Adopt Jev as default
  only at a threshold where all four metrics are ≥ the `model` run and median stage latency is lower. Otherwise
  record the sweep and keep `model`.

## Risks / Trade-offs

- [The wire format is unverified: planning worked from secondary sources] → Task 1.1 confirms it against the
  official reference before any client code; the judge seam isolates the change to one class.
- [A new external processor sees user questions, which may name clients or amounts] → Question text only, no
  identifiers. Off by default. The DECISIONS entry records that enabling it in any non-lab deployment needs a
  data-processing review.
- [A young vendor: outages, API churn, pricing changes] → Fallback to the chat judge on any failure; the provider
  is a single setting to revert.
- [Confidence is calibrated by the vendor, not for our labels] → The threshold is chosen by the sweep, not assumed,
  and the trace records every rejected answer so it can be revisited from real turns.
- [Two judges in series could double latency when Jev is slow] → Jev's slice is capped at 1 s inside the existing
  5 s deadline; the worst case is unchanged.
- [The new eval cases change the selection baseline] → Re-accepted deliberately, with both providers' runs in the
  same commit, so the regression gate compares like with like.

## Migration Plan

1. Merge with `IntentProvider = model`: no behaviour change; the new eval cases establish the baseline.
2. With a key, run the sweep locally and record it in DECISIONS.md.
3. If the rule in 8 passes, change the compose default to `jev` in a separate commit, with the numbers.
4. Rollback: `INTENT_PROVIDER=model` (no deploy), or revert the default commit.

## Open Questions

- Exact endpoint path, auth header and field names for a Choice request and response. Resolved by task 1.1; it
  changes only `JevClient`, not the seam, the specs, or the tasks.
- Whether Jev accepts a description per option or only labels. If only labels, the descriptions go into the state
  text; the judge contract is the same.
- Pricing per call, for the DECISIONS entry. It does not change the decision rule, which is quality and latency.
