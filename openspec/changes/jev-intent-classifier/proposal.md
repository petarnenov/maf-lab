# Proposal

## Why

Intent is decided by two mechanisms that disagree about what a question is. English regexes in
`IntentClassifier` decide first; everything they miss — every Bulgarian question, every paraphrase without their
keywords — goes to a chat model (`gemma4:31b`, median 479 ms per DECISIONS.md) asked for one word. The regexes are a
second source of truth that must be kept in step with the model's prompt, they only speak English, and neither stage
can say how sure it is.

TypeSafe's Jev (public since 2026-09-15) is built for exactly this: a question with a fixed answer space, answered
with a label, a probability for every option and a confidence, never anything outside the schema, at a published
~300 ms. One judge that classifies by meaning replaces both stages.

## What Changes

- **BREAKING — the rules stage is removed.** The English regexes no longer classify anything. Every question, in any
  language, is classified by Jev with one **Choice** question over the five intents (Procedural, Mixed, Data,
  ChitChat, Other).
- **BREAKING — the chat-model classifier is removed.** No `gemma4:31b` call, no `Agent:IntentModel`, no prompt
  parsing. There is no fallback classifier.
- **Confidence becomes part of acceptance.** An answer counts only at or above a configured threshold. Below it — and
  on failure, timeout or an unknown label — the turn has no recognised intent and nothing is forced, which is
  today's failure behaviour. The model can still call `search_documents` on its own.
- **BREAKING — a Jev key is required.** The stack refuses to start without `TYPESAFE_API_KEY`, as it needs
  `OLLAMA_API_KEY` today. CI runs against a Jev-shaped stub, so it stays key-free.
- **Only the question leaves the system.** The request carries the question text and the fixed intent descriptions,
  never the firm, principal, history or retrieved content.
- **Trace records the judge.** The intent event carries the raw answer, confidence, per-intent probabilities and,
  when nothing was accepted, why.
- **Evals that measure the classifier.** `evals/selection.jsonl` (24 English cases) gains Bulgarian and paraphrased
  cases, and the selection and injection suites are re-baselined under Jev. The results are recorded; a regression
  is reported for a decision, not reverted automatically.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `chat-agent`: "Conditional forced retrieval" — one confidence-gated decision judge replaces the rules and model
  stages; failure or low confidence forces nothing; only the question text reaches the judge; missing credentials
  refuse start.
- `turn-tracing`: "Complete turn trace" — the intent event records the judge's answer, confidence, probabilities and
  rejection reason instead of a rules/model stage.

## Impact

- `src/Maf.Lab.Api/Agent/IntentClassifier.cs` — regexes and `Classify` deleted; `ForcesRetrieval`/`IsHowWhy` kept.
- `src/Maf.Lab.Api/Agent/ModelIntentClassifier.cs` — deleted, replaced by `JevIntentClassifier` and `JevClient`.
- `src/Maf.Lab.Api/Agent/AgentOptions.cs` — `IntentModel` removed; `IntentMinConfidence` and `Jev:*` added.
- `src/Maf.Lab.Api/Agent/ChatTurnRunner.cs`, `IntentDecision` — new trace fields; `IntentStage` removed.
- `src/Maf.Lab.Api/Program.cs`, `src/Maf.Lab.Eval/Hosting/EvalAgentHost.cs` — registration.
- `tests/` — `IntentClassifierTests` rewritten; `ApiFactory` and every test that relied on the rules classifying an
  English question use a scripted judge instead.
- `compose/docker-compose.yml` — `TYPESAFE_API_KEY` required, `INTENT_MODEL` removed.
- `compose/ollama-stub/server.py` — the classifier-marker branch replaced by a Jev-shaped endpoint.
- `Makefile` (`doctor`), README — the new required key.
- `web/src/monitor/` — intent row shows confidence instead of stage.
- `evals/selection.jsonl`, `evals/baseline.json`, `DECISIONS.md` (supersedes §18 and the intent-model entry).
- **New external service**: TypeSafe API, now on every turn. No new NuGet package.
- Requires TypeSafe Console access; the wire format must be confirmed against the official reference first.
