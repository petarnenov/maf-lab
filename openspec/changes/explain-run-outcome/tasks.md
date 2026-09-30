# Tasks

No TypeSafe Jev call is added or changed, so the Jev review checklist does not apply.

## 1. Contracts and agent

- [x] 1.1 Make `TestGenBudget` caps nullable (`long? MaxTokens`, `double? MaxCostUsd`). In `TestGenRequest.Validate`, accept null caps and reject a set cap `<= 0`. In `RunUsage` (`Spent`, `WouldExceed`), compare only against caps that are set. Verify: contract tests for no caps, only a cost cap, and cap 0 rejected; a `RunUsage` test where a 10M-token usage with no caps is not spent
- [x] 1.2 Add `ActivityType.Stopped` and `StoppedActivity { reason, lastAttempt, bestPct, notStarted? }`, plus `ActivityReporter.StoppedAsync`. Record it in `TestGenerationHandler` just before the report artifact on each completing path: target, attempts, pre-attempt budget break (with `notStarted = n`), and `BudgetExceededException`. Verify: handler tests for each path assert that the last activity entry is `stopped` with the right reason and fields, and that failed and canceled tasks record none
- [x] 1.3 Add the round cap to `Instructions.Attempt`. Add a "you wrote no test in attempt n; reading without writing is not progress" line to `Instructions.Feedback` when `TestsIn(diff)` is empty. Verify: `Instructions` tests for both texts
- [x] 1.4 Add `RoundNudgeChatClient` between `FunctionInvokingChatClient` and `BudgetedChatClient`. It counts model calls per attempt (reset in `BeginAttempt`) and, when 3 rounds remain, appends the fixed nudge message. It logs nothing about content. Verify: a unit test with a fake inner client that has a cap of 12 sees the nudge on call 10 only, and the count resets on the next attempt

## 2. Api

- [x] 2.1 Add nullable `BudgetTokens`/`BudgetCostUsd` to `TestGenRunRow`, and `Budget` to `RunSummary`. Verify: `DatabaseInitializer` test that an existing database gains both columns, and old rows read as unlimited
- [x] 2.2 Add an optional `budget` to `StartRunRequest`. Validate it in `TestGenRuns.StartAsync` (`400` validation problem naming `budget`), persist it, and pass it to the task unchanged. Remove `TestAgent:Budget` from options, `appsettings.json` and the `/api/coverage/models` response. Verify: api tests for start with no budget (the task has null caps), a cost-only budget, and cap 0 rejected with no run and the threshold unchanged
- [x] 2.3 When a task completes, set `Reason = report.StopReason`. `RunVerifier` keeps it for `completed_no_change` and `candidate`, and replaces it only on `verification_failed`. Verify: api tests where an empty diff with `budget` ends `completed_no_change` with reason `budget`, a candidate keeps `target`, and a verification failure overwrites the reason
- [x] 2.4 In `RunActivityProjection`, map a `stopped` row to `STEP_FINISHED` for the open step plus `CUSTOM maf-lab/testgen-stopped`, and put `reason` and `budget` in the `STATE_SNAPSHOT` summary. Verify: a projection test for the "stop closes the timeline" scenario (event order, then `RUN_FINISHED`), plus a replay test

## 3. Web

- [x] 3.1 Add optional Max tokens and Max cost fields to `ModelPicker`, empty by default. Mark invalid values and disable Start until they are fixed. Replace the cap text with "Budget: unlimited — stops after at most 5 attempts" or the entered caps, with a warning when the estimate is above the cost cap. Send `budget` from `RaiseThresholdDialog`. Verify: Vitest for the default being unlimited, a cost-only request body, 0 disabling Start, and the warning
- [x] 3.2 Add `STOP_LABELS` in `format.ts`. `RunStatus` shows the reason in words. The Activity header shows the budget ("unlimited") and "State · reason" after the end. `runStream` reduces `maf-lab/testgen-stopped` into a closing timeline item. Verify: `RunActivity.test.tsx` for the budget-stopped scenario (header and last item), and `RunStatus.test.tsx` for "No change · budget"
- [x] 3.3 Turn `useRunStream` into a ref-counted registry: one `fetch` per run id, state kept for late subscribers, closed on the last unsubscribe. Verify: `useRunEvents.test.tsx` shows two subscribers opening one request, unmounting both closing it, and a remount getting the current state at once
- [x] 3.4 Tree row: a live marker (a pulsing dot with state, n/N and phase) from the shared stream for at most 3 most recently updated active runs. Other active rows keep the static badge, and the tree refetches every 10 s while any run is active. After a run ends, show a last-outcome label with the reason as a tooltip while `run.updatedAt > measuredAt` and the run is not a candidate. Respect `prefers-reduced-motion`. Verify: Vitest for the marker updating on a `STATE_SNAPSHOT`, the label after `completed_no_change`, and the fallback beyond 3 runs

## 6. Threshold above coverage means "reach it" (added during apply)

- [x] 6.1 `ThresholdControl`: open the confirmation for any value above coverage (raise, keep or lower), and for "Use default" when the default is above coverage (target: the default). Add "Save without a run" to the confirmation's first step. Verify: `ThresholdControl.test.tsx` covers Save with the default on a file below it (dialog, target 80%), a lower value still above coverage (dialog), a lower value coverage meets (saves at once), "Use default" below coverage (dialog), and "Save without a run" (PUT, no run)
- [x] 6.2 Api: an accepted start whose `pct` equals the configured default clears the override instead of storing one. Verify: an api test that starts a run to the default and finds no override row, with the effective threshold still the default

## 4. Verification

- [x] 4.1 `make test`, `make lint` pass
- [x] 4.2 Live check on http://localhost:7171: start a run on a below-threshold file with no budget. Watch the tree marker move and the Activity modal end with a closing line. Then start one with a tiny cost cap (e.g. $0.001) and see it end "No change · budget" on the row, in the status and in the modal — Done: a $0.001-capped run ended "No change · budget" on the row, the status and the modal (closing line "Stopped before attempt 1: the budget would be exceeded."), with the live row marker seen during it; an unlimited run started from the UI ran all 5 attempts with no caps and was canceled. That run also showed the model still writing no test on this Redis-backed file (see the final report).
- [x] 4.3 `openspec validate explain-run-outcome --strict` passes

## 5. Documentation

- [x] 5.1 In `docs/http-api.md`, add the optional `budget` to `POST /api/coverage/runs`, remove `maxTokens`/`maxCostUsd` from `GET /api/coverage/models`, add `budget` and `reason` to the run summary, and add `CUSTOM maf-lab/testgen-stopped` to the events row. Edit the source, not a `generated:` block. Verify: the rows read correctly
- [x] 5.2 Run `make docs`, then `make docs-check`. Verify: `make docs-check` exits 0
