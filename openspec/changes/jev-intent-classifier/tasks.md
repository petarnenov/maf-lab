# Tasks

## 1. Confirm the Jev API

- [ ] 1.1 With TypeSafe Console access, read the official API reference and record in design.md (Decisions §2) the
      Choice request/response shape, auth header, endpoint and model name; verify with one `curl` against the live
      API using a throwaway question, and note whether per-option descriptions are supported
- [ ] 1.2 Save one real Choice response (no user data) as `tests/Maf.Lab.Tests/Fixtures/jev-choice.json` and verify
      it parses into label, per-label probabilities and confidence in a unit test

## 2. Judge seam (no behaviour change)

- [ ] 2.1 Introduce `IIntentJudge` and `JudgeResult`; move the chat-model call, prompt, `Parse` and 512-token budget
      from `ModelIntentClassifier` into `ChatModelIntentJudge` unchanged; verify `IntentClassifierTests` and
      `TurnTraceTests` pass without edits
- [ ] 2.2 Extract the five intent descriptions into one shared constant used by the chat prompt; verify the prompt
      text is byte-identical (existing marker test plus a snapshot assertion)
- [ ] 2.3 Make `ModelIntentClassifier` a stage runner with one deadline from `IntentTimeoutSeconds` passed to each
      judge; verify with a test double that a slow judge is abandoned at its slice and the total never exceeds the
      timeout

## 3. Jev judge

- [ ] 3.1 Add `Agent:IntentProvider`, `Agent:IntentMinConfidence` and `Agent:Jev:{Endpoint,ApiKey,Model,TimeoutSeconds}`
      to `AgentOptions` with start-time validation; verify a test host with `IntentProvider=jev` and no key fails to
      start with a message naming `Agent:Jev:ApiKey`, and the default host starts unchanged
- [ ] 3.2 Implement `JevClient` (typed `HttpClient`) taking only the question string; verify with a fake handler that
      the serialised body contains the question and intent descriptions and no firm id, principal, history or
      document text
- [ ] 3.3 Implement `JevIntentJudge`: accept at or above the threshold, reject below with a reason, map errors and
      timeouts to a reason; verify unit tests for accept, below-threshold, HTTP error, timeout and a label outside the
      five (treated as unrecognised)
- [ ] 3.4 Wire fallback: Jev first, chat judge on rejection with the remaining budget; verify tests for each of the
      chat-agent delta scenarios "Confident judge decides", "Unsure judge falls back", "Failed judge falls back within
      the same timeout" and "No judge configured"
- [ ] 3.5 Register by provider in `Maf.Lab.Api/Program.cs` and `Maf.Lab.Eval/Hosting/EvalAgentHost.cs`; verify both
      hosts resolve the classifier under `model` and `jev`
- [ ] 3.6 Ensure logs carry only provider, stage, duration, accepted and confidence; verify a test that captures log
      output for a Jev turn and asserts the question text is absent

## 4. Trace and UI

- [ ] 4.1 Add `IntentStage.Judge`, `Confidence`, `Probabilities` and `FallbackReason` to `IntentDecision` and write
      them into the `intent` event in `ChatTurnRunner`; verify `TurnTraceTests` cases for the two new turn-tracing
      scenarios
- [ ] 4.2 Show judge and confidence in the monitor's intent row and probabilities in its detail pane
      (`web/src/api/types.ts`, `web/src/monitor/`); verify with a Vitest case using a Jev fixture event, and
      `make lint-web`

## 5. CI stub and compose

- [ ] 5.1 Add a Jev-shaped endpoint to `compose/ollama-stub/server.py` reusing `classify()`, confidence 0.9, or 0.3
      when the question contains `__low_confidence__`; verify with a local request for each case
- [ ] 5.2 Add `INTENT_PROVIDER` (default `model`), `INTENT_MIN_CONFIDENCE` and `TYPESAFE_API_KEY` (default empty)
      to `compose/docker-compose.yml`; verify `make` starts with defaults and `make verify` passes
- [ ] 5.3 Run `make ci-e2e` once with `INTENT_PROVIDER=jev` pointed at the stub and verify a Bulgarian procedural
      question forces retrieval via the judge, and a `__low_confidence__` question is decided by the model stage

## 6. Evals and decision

- [ ] 6.1 Add rule-miss cases to `evals/selection.jsonl` (Bulgarian counterparts of existing cases across all
      categories, keyword-free English paraphrases, negatives); verify with a dry run that every new case is `Other`
      under `IntentClassifier.Classify`
- [ ] 6.2 Run `make eval SUITE=selection` and `SUITE=injection` with `INTENT_PROVIDER=model` and accept the
      baseline with `make eval-accept`; verify `evals/baseline.json` changes only in selection
- [ ] 6.3 With a key, sweep `INTENT_PROVIDER=jev` at thresholds 0.4, 0.5, 0.6, 0.7, 0.8 on `selection` and
      `injection`, reading stage latency from the traces; verify the table (metrics, median/p90 ms, fallback rate per
      threshold) is written to DECISIONS.md
- [ ] 6.4 Apply the decision rule in design.md §8: set the default threshold, and switch the compose default to `jev`
      only if it holds (separate commit with the numbers); verify `make eval SUITE=selection` passes the regression
      gate against the committed baseline

## 7. Records and checks

- [ ] 7.1 Add a DECISIONS.md section: why a judge seam over M.E.AI, own client over the community SDK, shared
      deadline, off-by-default, question-only egress, and the data-processing caveat; verify it is in the same
      commit as the code
- [ ] 7.2 Run `make specs`, `make lint` and `make test`; verify all pass
