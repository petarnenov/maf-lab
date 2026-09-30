# Tasks

## 1. Contracts

- [x] 1.1 Add `ActivityType.Resumed` and `TestGenFailure.Interrupted` in `AgentContracts.cs`; verify the contract tests still pass

## 2. Agent

- [x] 2.1 Add `TaskCheckpointStore` (checkpoint get/save/delete, lease take/renew/release with compare-and-set) over Redis; verify with a unit test against a substitute or real Redis-free fake that a taken lease is not taken twice and a foreign lease is not renewed or released
- [x] 2.2 Let `ActivityReporter` start from a given sequence number; verify with an `ActivityReporterTests` case that the first entry after a start of 41 is 42
- [x] 2.3 Split the handler loop into a starting state (fresh: workspace + baseline; resumed: workspace from `workingDiff`, usage, reporter sequence, next attempt) and write the checkpoint at accept, after the baseline and after every attempt; take and renew the lease for the task's life, release it at the end; verify existing `TestAgentTests` still pass
- [x] 2.4 Add the resume path: its own event queue drained into the task store through `TaskProjection.Apply` under the notifier's task lock, a `resumed` entry first, cancel still reaching it; verify with a test that stops the agent host during attempt 2, starts a new host over the same store, and sees the task complete with attempt 1 logged once, a `resumed` entry for attempt 2 numbered after the earlier entries, and usage carried over
- [x] 2.5 Add the `TaskRecovery` background service (at start and every 15 s): resume tasks with a lapsed lease and a checkpoint, fail those without one with `interrupted`, leave live and canceled tasks alone; verify with tests for "nothing to resume from" and "a live task is not taken"

## 3. Api and web

- [x] 3.1 Project `resumed` as `STEP_FINISHED` + `CUSTOM maf-lab/testgen-resumed` `{attempt}` and map `interrupted` in `TestGenRuns.Code`; verify with a projection test and an api test
- [x] 3.2 Show the resumed event as a notice in the run timeline (`runStream.ts`); verify with a Vitest case

## 4. Verification

- [x] 4.1 Run `make test` and `make lint`; both pass
- [ ] 4.2 Live check: start a run from the Coverage page, restart the test agent (`docker compose restart test-agent`) during an attempt, and see the run resume on its own within about a minute, the timeline showing the restart notice, and the run reaching a final state

## 5. Documentation

- [x] 5.1 Update `docs/shared-state.md` (test agent tasks, checkpoints, leases; how a task survives an agent restart) and the run event stream row in `docs/http-api.md` (`CUSTOM maf-lab/testgen-resumed`), then run `make docs` and `make docs-check`; both succeed
