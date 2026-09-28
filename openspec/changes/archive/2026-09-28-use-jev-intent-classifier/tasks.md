# Tasks

## 1. Jev client and credential

- [x] 1.1 Add `JevOptions` (section `Jev`: `Endpoint` = `https://api.typesafe.ai`, `Model` = `jev-1.13.0`,
      `MinConfidence` = 0.5, `TimeoutSeconds` = 2) and request/response DTOs for one Choice question in
      `src/Maf.Lab.Api/Agent/`. Verify `make lint` builds warnings-as-errors clean and a unit test round-trips the
      documented example response (`choice`, `probabilities`, `confidence`, `model`) through the DTOs.
- [x] 1.2 Add `JevAuthHandler` (a `DelegatingHandler`) that reads `JEV_MAF_LAB` from `IConfiguration` and sets
      `Authorization: Bearer` on each request, and register the named `"jev"` `HttpClient` with it in `Program.cs` and
      `EvalAgentHost`. Verify with a fake inner handler that the header carries the key and the serialised body does not.
- [x] 1.3 On startup, when `JEV_MAF_LAB` is absent or empty, log one warning naming the variable (never a value).
      Verify with a test using a capturing logger that the warning appears once and contains no key-like value.

## 2. Classifier

- [x] 2.1 Implement `JevIntentClassifier : IIntentClassifier`: builds the request from the design (question only in
      `state.user_question`, fixed instructions and five option descriptions, pinned model), races the call against
      `Task.Delay(timeout)`, maps the choice to `Intent`, applies the confidence floor, and returns `Other` with a reason
      for low confidence, timeout, HTTP error status, transport failure, unknown option and missing key (no request
      made). Verify each branch with a unit test against a fake `HttpMessageHandler`, including a handler that ignores
      cancellation returning within the timeout.
- [x] 2.2 Reshape `IntentDecision` to `(Intent, Choice, Probabilities, Confidence, Model, DurationMs, Reason)` and
      delete `IntentStage`. Verify the solution builds and no reference to `IntentStage` or `RawAnswer` remains
      (`grep -rn "IntentStage\|RawAnswer" src tests` returns nothing).
- [x] 2.3 Delete `ModelIntentClassifier`, the four regexes and `IntentClassifier.Classify`, keeping `ForcesRetrieval`
      and `IsHowWhy`; delete `AgentOptions.IntentModel` and `IntentTimeoutSeconds`; register `JevIntentClassifier` in
      `Program.cs` and `EvalAgentHost`. Verify `grep -rn "ModelIntentClassifier\|IntentModel\|IntentTimeoutSeconds\|Classify(" src`
      finds nothing and `make lint` passes.
- [x] 2.4 Verify no log statement in the classifier or handler includes the question or the key: a unit test with a
      capturing logger classifies a question containing a sentinel string with a sentinel key and asserts neither
      appears in any log entry.

## 3. Trace

- [x] 3.1 Change the `intent` event in `ChatTurnRunner` to carry `intent`, `forcedRetrieval`, `forcedTool`, `choice`,
      `probabilities`, `confidence`, `model`, `durationMs`, `reason`, with the summary
      `Intent <X> (jev <confidence>, <ms> ms)`. Verify `TurnTraceTests` asserts the new fields for a procedural turn and
      for a below-floor turn, and that the trace JSON never contains the test key.

## 4. Tests and CI stub

- [x] 4.1 Rewrite `tests/Maf.Lab.Tests/IntentClassifierTests.cs` for Jev (covers 2.1's branches), delete
      `AgentUnitTests.Intent_classification`, and change `ApiFactory` to a fake `"jev"` handler plus a scripted chat
      model that decides "must have searched" from the recorded intent decision rather than the regexes. Verify
      `make test` passes.
- [x] 4.2 Add `POST /v1/systemone` to `compose/ollama-stub/server.py` (keyword classification of `state.user_question`,
      Choice answer with confidence 1.0, `model: "jev-stub"`, 401 without a bearer token) and remove the `/api/chat`
      classifier-marker path. Set `Jev__Endpoint: http://ollama:11434` and `JEV_MAF_LAB: ci-stub-key` in
      `compose/docker-compose.ci.yml`. Verify `make CI_MODE=1` brings the stack up and `make ci-e2e` (or `make verify`
      in CI mode) passes with a procedural turn forced.

## 5. Configuration, tooling, docs

- [x] 5.1 In `compose/docker-compose.yml`, replace `Agent__IntentModel: ${INTENT_MODEL:-gemma4:31b}` with
      `JEV_MAF_LAB: ${JEV_MAF_LAB:-}` beside `OLLAMA_API_KEY`, with a comment that it is passed through, never written
      to a file. Verify `docker compose config` shows the variable without the value committed anywhere
      (`git grep -n "$JEV_MAF_LAB"` finds nothing — run it without echoing the value).
- [x] 5.2 Extend `scripts/doctor.sh` to report `JEV_MAF_LAB` set/missing like `OLLAMA_API_KEY`, and the `make doctor`
      help text. Verify `env -u JEV_MAF_LAB make doctor` marks it missing and `make doctor` with it set prints "set" and
      no value.
- [x] 5.3 Add `JEV_MAF_LAB: ${{ secrets.JEV_MAF_LAB }}` to `.github/workflows/evals.yml` with a require-step like the
      one for `OLLAMA_API_KEY`. Verify the workflow YAML parses (`make lint` or `actionlint` if present) and the step
      prints only "JEV_MAF_LAB is set".
- [x] 5.4 Update `DECISIONS.md`: replace the "Intent classification" Models row with Jev (`jev-1.13.0`, TypeSafe
      API, key `JEV_MAF_LAB`), add a section for this change (sole classifier, confidence floor, pinned version, why a
      typed `HttpClient` rather than a Microsoft Agent Framework / Microsoft.Extensions.AI abstraction, CI stub, the
      planning probe numbers), and mark §18's two-stage design as superseded. Update `CLAUDE.md` and
      `.github/copilot-instructions.md` to name `JEV_MAF_LAB`. Verify the rows read correctly together.

## 6. Verification against the real model

- [x] 6.1 With `JEV_MAF_LAB` set, run `make eval SUITE=selection` three times. Verify recall, precision, exactMatch
      and negativeAccuracy are at or above `evals/baseline.json`, and that every turn's `intent` event names
      `jev-1.13.0`. Record the three runs and the intent latency median/p90 from the traces in DECISIONS.md.
- [x] 6.2 Run `make eval SUITE=injection` and `make eval SUITE=all`. Verify every suite passes; for a generation
      failure, re-run before treating it as a regression (the known ±0.09 faithfulness noise).
- [x] 6.3 With the stack up, ask "Каква е процедурата, когато липсва фий схедюл?" and "hi". Verify the first is
      classified procedural by Jev and forces `search_documents`, the second is classified chitchat by Jev, and no
      chat request in either trace is a classification request.
- [x] 6.4 Restart the api without `JEV_MAF_LAB`. Verify it starts, logs the missing-key warning once, and a
      procedural question still answers with `reason: "no key"` and nothing forced.
- [x] 6.5 Search the logs of 6.1–6.4, the stored traces and the repository for the key value (without printing it,
      e.g. `grep -c`). Verify every count is 0.
