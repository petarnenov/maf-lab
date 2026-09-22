# Tasks

## 1. Pin what is wrong before changing it

- [x] 1.1 Add a test to `tests/Maf.Lab.Tests/ZeroResultTurnTests.cs` for a turn whose first
      `search_documents` returns no results and whose second returns documentation the answer cites,
      asserting the turn carries no `zero_retrieval_results` signal. Verify it **fails** against the
      current code — that failure is the defect, reproduced.
- [x] 1.2 Verify the two existing cases in that file still pass unchanged: a turn that searched and
      got nothing carries the signal, a turn that searched and got sources does not.

## 2. Make the signal describe the turn

- [x] 2.1 In `src/Maf.Lab.Api/Agent/ChatTurnRunner.cs`, replace `state.ZeroResults` with a flag that
      records that a `search_documents` call completed without error, whatever it returned, and
      rename it to say so. Verify `make lint` builds warnings-as-errors clean.
- [x] 2.2 In `src/Maf.Lab.Api/Agent/TurnSignals.cs`, compute `ZeroRetrievalResults` from that flag
      and the turn's `sourceCount`, which `Compute` already receives, and rename the parameter to
      match. Verify the test from 1.1 now passes.
- [x] 2.3 Update the unit cases in `tests/Maf.Lab.Tests/AgentUnitTests.cs` that call
      `TurnSignals.Compute` directly, so they exercise the new argument. Verify they cover a turn
      that searched and found nothing, a turn that searched and found something, and a turn that
      never searched. Verify with `make test-dotnet`.
- [x] 2.4 Verify `FeedbackApiTests.Production_signals_put_turns_in_the_queue` still passes: its
      zero-result turn searches once, gets nothing and cites nothing, so it must still be flagged.

## 3. Confirm against the turn that started this

- [x] 3.1 Run `make lint` and `make test` and verify both pass.
- [x] 3.2 With the stack running, ask the Bulgarian question that produced the observed turn —
      "Каква е процедурата при липсваща фий схема?" — and verify the turn still answers with
      sources, its trace still shows the first search returning nothing and the unusable
      translation, and it no longer carries `zero_retrieval_results`.
- [x] 3.3 Ask a question the corpus cannot answer and verify that turn does carry the signal and
      appears in `/admin/feedback`, so the fix narrowed the condition rather than disabling it.
- [x] 3.4 Run `make eval SUITE=all` and verify every suite passes. Note that the generation gate is
      known to fail on roughly half of runs from its own judge noise — re-run before treating a red
      generation result as a regression.
