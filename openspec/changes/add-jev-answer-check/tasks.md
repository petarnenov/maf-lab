# Tasks

## 1. The check

- [x] 1.1 Add `AnswerCheckOptions` (`Jev:AnswerCheck`: `Enabled` true, `TimeoutSeconds` 3, `MinRelevant` 0.5,
      `MinGrounded` 0.5, `MaxSourceChars` 12000) and `JevAnswerCheck` in `Maf.Lab.Api/Agent/Jev`: the state
      `{user_question, answer, sources}`, the two criteria Nouls in the guard's style, one request through `JevClient`,
      the verdict and signals of design D4, and every failure as `unchecked` with its reason. Register it with the
      classifier so the api and the eval host both have it.
- [x] 1.2 Add `TurnSignal.AnswerNotRelevant` / `AnswerNotGrounded` and `TraceKinds.AnswerCheck`.

## 2. The turn

- [x] 2.1 `ChatTurnRunner`: record what the model read (per search excerpt, other results whole, after the guard);
      run the check after the stream only for a turn that reached the model, did not fail, is not waiting for a
      confirmation and has a non-empty answer; trace `answer.check` before `sources`; add its signals; carry the
      outcome on `TurnResult`. No content in logs.
- [x] 2.2 `FakeJev` answers `answer_relevant` / `answer_grounded` (default pass, settable); the CI stub answers them
      too. Tests: a normal turn records a pass event; a low-grounded answer adds the signal and enters the review
      queue; a refused prompt and a turn awaiting confirmation run no check; Jev down → unchecked and the turn still
      answers; disabled → unchecked, no request; the event holds neither the answer nor an excerpt; a withheld excerpt
      is not sent. Existing tests that count Jev requests per turn account for the check.

## 3. Statistics and screens

- [x] 3.1 `JevStatistics`: the `answer` site in the overview (requests, unavailable, latency, timeline) and the
      `AnswerCheck` section; contract optional. Tests: counts, unavailable, disabled not a request, section numbers.
- [x] 3.2 Web: `answer.check` kind colour and its own latency bar on the timeline, the header chip for a failed
      verdict, the `answer` site and the Answer check section (KPIs and latency histogram) on `/admin/jev`, the new
      signal labels. Vitest for each.

## 4. Eval and docs

- [x] 4.1 `GenerationSuite`: the verdict per case in progress and failures; `jevChecked`, `jevGroundedAgreement`,
      `jevRelevantAgreement` (omitted when nothing was checked). Not run here (paid).
- [x] 4.2 `docs/trace-events.md` (the event, typical order), `DECISIONS.md` (the floors and timeout as provisional,
      latency added to the turn, rollback).
- [x] 4.3 Verify: `dotnet build -warnaserror`, `dotnet test` (605 green), web `vitest` (305 green), `lint`, `tsc`;
      `openspec validate add-jev-answer-check --strict`.
