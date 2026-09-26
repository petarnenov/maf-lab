# Tasks

## 1. Confirm the Jev API

- [ ] 1.1 With TypeSafe Console access, read the official API reference and record in design.md (Decisions §3) the
      Choice request/response shape, auth header, endpoint and model name; verify with one `curl` against the live
      API using a throwaway question, and note whether per-option descriptions are supported
- [ ] 1.2 Save one real Choice response (no user data) as `tests/Maf.Lab.Tests/Fixtures/jev-choice.json` and verify
      it parses into label, per-label probabilities and confidence in a unit test

## 2. Test seam first

- [ ] 2.1 Add a scripted `IIntentClassifier` for tests (keyword → intent, confidence 0.9) and register it in
      `ApiFactory` and the unit-test hosts; verify `make test-dotnet` passes before anything is deleted

## 3. Jev classifier

- [ ] 3.1 Add `Agent:IntentMinConfidence` and `Agent:Jev:{Endpoint,ApiKey,Model}` to `AgentOptions`, set
      `IntentTimeoutSeconds` default to 2, with start-time validation; verify a host with no key fails to start with a
      message naming `Agent:Jev:ApiKey`
- [ ] 3.2 Implement `JevClient` (typed `HttpClient`) taking only the question string; verify with a fake handler that
      the body contains the question and the intent descriptions and no firm id, principal, history or document text
- [ ] 3.3 Implement `JevIntentClassifier`: accept at or above the threshold; below it, on HTTP error, timeout (kept
      `Task.WhenAny` race) or unknown label return `Other` with a reason; verify unit tests for each case plus a
      server that ignores cancellation costing no more than the timeout
- [ ] 3.4 Register it in `Maf.Lab.Api/Program.cs` and `Maf.Lab.Eval/Hosting/EvalAgentHost.cs`; verify both hosts
      resolve it
- [ ] 3.5 Verify with a log-capture test that a classified turn logs duration, accepted and confidence, and never the
      question text

## 4. Remove the rules and the chat classifier

- [ ] 4.1 Move `ForcesRetrieval` and `IsHowWhy` to a static `Intents` class; delete the regexes and
      `IntentClassifier.Classify`; verify `grep -rn "IntentClassifier.Classify" src tests` is empty and the build
      passes
- [ ] 4.2 Delete `ModelIntentClassifier`, `AgentOptions.IntentModel` and `IntentStage`; move the intent descriptions
      into the shared constant used by `JevClient`; verify `make lint-dotnet`
- [ ] 4.3 Rewrite `IntentClassifierTests` for `JevIntentClassifier` and update `TurnTraceTests`/`AgentUnitTests`
      assertions that named the rules or model stage; verify `make test-dotnet`

## 5. Trace and UI

- [ ] 5.1 Reshape `IntentDecision` (confidence, probabilities, reason; no stage) and write it into the `intent` event
      in `ChatTurnRunner`; verify `TurnTraceTests` for "Intent decided by the judge" and "Intent not accepted"
- [ ] 5.2 Show confidence in the monitor's intent row and probabilities in its detail pane (`web/src/api/types.ts`,
      `web/src/monitor/`, fixtures); verify Vitest and `make lint-web`

## 6. CI stub, compose and tooling

- [ ] 6.1 Replace the classifier-marker branch in `compose/ollama-stub/server.py` with a Jev-shaped endpoint reusing
      `classify()`, confidence 0.9, or 0.3 on `__low_confidence__`; verify with a local request for each case
- [ ] 6.2 In `compose/docker-compose.yml` remove `INTENT_MODEL`, add `TYPESAFE_API_KEY` and `INTENT_MIN_CONFIDENCE`;
      point CI's compose override at the stub; add the key to `make doctor` and the README; verify `make` with a key
      starts and `make verify` passes, and without one `make doctor` names it
- [ ] 6.3 Run `make ci-e2e`; verify a Bulgarian procedural question forces retrieval and a `__low_confidence__`
      question forces nothing

## 7. Evals

- [ ] 7.1 Add Bulgarian counterparts across all categories, keyword-free English paraphrases and negatives to
      `evals/selection.jsonl`; verify the file parses and the eval runner loads every case
- [ ] 7.2 With a key, sweep `INTENT_MIN_CONFIDENCE` 0.4–0.8 on `selection` and `injection`, reading classification
      latency from the traces and median turn latency before/after; verify the table is written to DECISIONS.md
- [ ] 7.3 Set the chosen threshold and timeout defaults and run `make eval-accept`; verify `evals/baseline.json`
      changes only in `selection` and `injection`, and any metric below the old baseline is called out in DECISIONS.md

## 8. Records and checks

- [ ] 8.1 Add a DECISIONS.md section superseding §18 and the intent-model row: one judge, no rules or fallback, own
      client, question-only egress, required key, data-processing caveat; verify it is in the same commit as the code
- [ ] 8.2 Run `make specs`, `make lint` and `make test`; verify all pass
