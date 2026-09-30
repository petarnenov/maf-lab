# Proposal

## Why

Run `r_8a6e914c…` on `src/Maf.Lab.A2A/RedisPushConfigStore.cs` looked stuck, but it had ended. The Activity modal's
last line was "Attempt 2: measuring" with nothing after it. The header said "No change · attempt 2/5". The tree row
showed no sign of a run. What actually happened:
- In each attempt, `glm-5.3-flash:cloud` spent the whole 12-round tool cap reading files and never called
  `write_file`.
- Attempt 2 started from an empty context and read the same files again.
- The pre-attempt check then found that a third attempt would cross the 400 000-token cap, and the task completed
  with `stopReason: budget`.
- None of that reached the screen. The api drops the stop reason when a run ends `completed_no_change`, and the
  agent records no activity entry when it stops.

The admin could not tell a finished run from a stuck one, and the agent wasted its attempts.

## What Changes

- **The run says why it stopped.** When a task completes, its stop reason (`target`, `attempts`, `budget`) becomes
  the run's reason, including for `completed_no_change`. The agent records a final `stopped` activity entry
  ("Stopped: budget would be exceeded before attempt 3"), so the timeline ends on a closing line, not on an open
  phase. The file's run status and the Activity header show the reason in user-facing words.
- **The agent writes before it runs out of rounds.**
  - The instructions state how many tool rounds an attempt has, and ask the model to write early.
  - When only a few rounds are left, the next model call is told to write now.
  - When an attempt added or changed no test, the next attempt's feedback says so plainly.
  - The tool-round cap per attempt stays 12, and the attempt cap stays 5.
- **The admin sets the run's budget when picking the model.** The model picker gains two optional fields: max tokens
  and max cost (USD). Both are empty by default, which means **unlimited**. Then the only limit is the attempt cap
  of 5 (plus the run's deadline). The request carries the budget, and the run stores and shows it. The configured
  `TestAgent:Budget` default is removed. **BREAKING** (api contract): `TestGenBudget` caps become optional; the agent
  accepts a task without caps.
- **The tree row shows a live run.** A file with an active run shows a live marker (a pulsing dot with state,
  attempt and phase) that follows the run's AG-UI stream, as the file view already does. After the run ends, the
  row keeps a short label of the last outcome ("no change", "failed", "candidate") until the next measurement of
  the file.

- **A threshold above coverage means "reach it".** Pressing Save with any threshold above the file's current coverage
  opens the run confirmation, whether the value raises, keeps or lowers the stored one. Today only a raise does, so a
  file that sits below the default cannot be sent to the agent by saving the default. The same applies to "Use
  default" when the default is above coverage. A run whose target equals the default leaves the file on the default.
  The confirmation gains "Save without a run", so a threshold can still be changed without spending a run.
  (Added during apply, at the user's request.)

Assumption recorded: "loop limit 5" means the attempt cap, which is already 5 and stays 5. It is not the per-attempt
tool-round cap: lowering that to 5 would make the stall above worse.

## Capabilities

### New Capabilities
<!-- None. -->

### Modified Capabilities
- `test-generation-agent`: task input budget becomes optional (unlimited when absent); stop conditions record a
  final `stopped` activity entry with the reason; the attempt loop tells the model its round cap, nudges it to write
  when few rounds remain, and feeds back an attempt that wrote nothing.
- `test-generation-runs`: the run carries its budget and keeps the task's stop reason as its reason for
  `completed_no_change` and goal-not-reached candidates; the AG-UI stream carries the `stopped` entry.
- `model-selection`: the picker takes an optional budget, unlimited by default; the estimate text describes the
  chosen limit.
- `coverage-threshold`: saving a threshold above coverage opens the run confirmation whatever the direction of the
  change, including "Use default"; the confirmation can save without a run; a run to the default keeps the default.
- `coverage-dashboard`: tree rows show a live run marker from the AG-UI stream and the last outcome after it ends;
  the run status and Activity header show the stop reason and the budget.

## Impact

- Agent: `src/Maf.Lab.TestGen/AgentContracts.cs` (nullable caps, validation, `ActivityType.Stopped`),
  `src/Maf.Lab.TestAgent/{TestGenerationHandler,BudgetedChatClient,Instructions,ActivityReporter}.cs`.
- Api: `TestGenRuns.StartAsync`, `CoverageEndpoints.StartRunRequest`, `RunVerifier` (reason on no-change),
  `RunActivityProjection` (the `stopped` entry), `SaveThresholdAsync` (a target equal to the default clears the override), `RunSummary` + `TestGenRunRow` (budget columns, added by the
  additive schema pass), `appsettings.json` (`Budget` removed).
- Web: `ThresholdControl.tsx`, `ModelPicker.tsx`, `RaiseThresholdDialog.tsx`, `RunStatus.tsx`, `RunActivity.tsx`, `CoverageTree.tsx`,
  `runStream.ts`/`useRunEvents.ts`, `format.ts`, CSS.
- Tests: agent handler tests (stop entry, nudge, unlimited budget), api run tests (reason, budget round trip),
  web tests for picker, tree marker and timeline end.

## Documentation impact

- `docs/http-api.md`: `POST /api/coverage/runs` body gains optional `budget: { maxTokens?, maxCostUsd? }`. `GET
  /api/coverage/models` no longer returns `maxTokens`/`maxCostUsd` (the default is unlimited). The run summary gains
  `budget`. The events row mentions the final `stopped` entry.
- README.md, CLAUDE.md, openspec/project.md and .github/copilot-instructions.md do not describe the run budget or the
  run outcome, so none of them changes.
