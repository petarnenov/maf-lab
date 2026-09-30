# Proposal

## Why

When an administrator picks a model for a test-generation run, the picker shows only the model and two empty budget
fields. The limits that actually bound the run — the attempt cap, the tool rounds per attempt, the test runs per
attempt, the deadline, the suspected-bug cap — are invisible, and the budget fields show nothing, so the
administrator cannot see what the run may do, nor change any of it except the caps. The cost estimate beside the
model is also wrong: it assumes about 1.6 re-reads of the file per attempt, while an attempt is a tool loop of up to
40 model calls, each re-sending the whole conversation, so real runs cost many times the estimate. It also ignores
the attempts the administrator could choose, since it always prices the configured cap.

## What Changes

- The model step of the "Reach N%?" dialog shows a **Limits** section with every constraint a run has, each filled
  with its default:
  - editable, validated in the browser and again on the server: max attempts (default 10, 1–10), tool rounds per
    attempt (default 40, 1–40), test runs by the model per attempt (default 2, 0–2), max tokens and max cost (USD);
  - the two caps show their default, **Unlimited**, as a checked box; unchecking it fills the field with the
    selected model's estimate so there is a sensible number to edit;
  - shown read-only: the target line coverage, the run deadline and the suspected bugs a run may report (3).
- `GET /api/coverage/models` returns these limits (default, minimum, maximum; the fixed ones as values) and, per
  model, the estimated tokens **per attempt** and **per tool round**, instead of one pre-multiplied estimate.
- `POST /api/coverage/runs` accepts optional `limits: { maxAttempts, toolRoundsPerAttempt, testRunsPerAttempt }`; a
  missing limit takes its default. The api stores the chosen limits on the run and passes them to the agent.
- The A2A task (`testgen.request/v1`) may carry `toolRoundsPerAttempt` and `testRunsPerAttempt`; the agent uses them
  instead of its own configuration, rejects values outside the contract's bounds, and takes the defaults when absent.
- The cost estimate is recalculated from measured runs: per attempt it prices the tool loop (tokens that grow with
  the rounds the attempt may take and with the file's size), for the attempts and rounds entered, at the selected
  model's prices. It updates as the administrator edits the limits, and still says it is an estimate.
- **BREAKING** (internal only): `ModelsDto.maxAttempts` and `ModelDto.estimate` are replaced by `limits` and
  `perAttempt`; the web app is the only caller and changes in the same commit.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `model-selection`: the picker shows all run limits with their defaults, lets the administrator lower the attempt,
  round and test-run limits, shows the caps' default as unlimited, and prices the estimate from the limits entered
  with a corrected per-attempt model.
- `test-generation-runs`: a start request may carry per-run limits; the api validates them, stores them on the run
  and passes them to the task.
- `test-generation-agent`: the task input may carry the tool-round and test-run limits; the tool-round cap comes from
  the task (bounded by the contract) rather than from agent configuration only.

## Impact

- Code: `src/Maf.Lab.TestGen` (`TestGenRequest`, `AttemptEstimate`, run-limit bounds), `src/Maf.Lab.TestAgent`
  (handler, tools, options), `src/Maf.Lab.Api` (models and runs endpoints, `CostEstimator`, `TestGenRuns`, run row),
  `web/src/coverage` (`ModelPicker`, `RaiseThresholdDialog`, `budget.ts`, a new limits helper), `web/src/api/types.ts`.
- Storage: two nullable columns on the run row (tool rounds, test runs per attempt), added by the existing additive
  column pass; old rows read as the defaults.
- Tests: xUnit for the contract bounds, the endpoints and the estimator; Vitest for the picker and the limits helper.
- No Jev call is added or changed. No package moves. No model changes.

## Documentation impact

- `docs/http-api.md`: the `/api/coverage/models` response (`limits`, `perAttempt`) and the `/api/coverage/runs`
  body (`limits`).
- `DECISIONS.md`: a new entry — per-run limits come from the task, bounded by the contract; the estimator's new
  constants and the runs they were fitted to. §60's "the cap is only in `TestAgentOptions`" becomes untrue and is
  superseded by the new entry.
- README.md, CLAUDE.md, openspec/project.md and .github/copilot-instructions.md: not affected — they do not describe
  the picker's fields or the estimate.
