# Proposal

## Why

The picker's Limits section shows the run deadline (2 h) and the suspected-bug limit (3) as read-only text, so an
administrator can see them but cannot set them for a run. Every other run limit is already a field filled with its
default; these two should be too.

## What Changes

- The model step shows two more editable limit fields, filled with their defaults and validated like the others:
  - **Run deadline (minutes)**: default and maximum the configured deadline (120), minimum 10;
  - **Suspected bugs per run**: default and maximum 3, minimum 0 (0: the run reports none).
  The target line coverage stays read-only: it is the threshold being raised.
- `GET /api/coverage/models` serves both as bounds (`deadlineMinutes`, `maxSuspectedBugs` become `{ min, max,
  default }`), like the other limits.
- `POST /api/coverage/runs` `limits` accepts `deadlineMinutes` and `maxSuspectedBugs`; the api validates them, stores
  them on the run, enforces the run's own deadline, applies the run's bug limit when it verifies the candidate, and
  passes the bug limit to the task. The run summary's `limits` carries both.
- The A2A task (`testgen.request/v1`) may carry `maxSuspectedBugs` (0–3, absent → 3). The agent's instructions, its
  `report_suspected_bug` tool and the test guardrails use the task's limit instead of the fixed 3.
- **BREAKING** (internal only): in the models response, `limits.deadlineMinutes` and `limits.maxSuspectedBugs` change
  from numbers to bounds; the web app is the only client and changes in the same commit.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `model-selection`: the deadline and the suspected-bug limit become editable fields with their defaults.
- `test-generation-runs`: a start request may carry the deadline and the bug limit; the api enforces the run's
  deadline and verifies with the run's bug limit.
- `test-generation-agent`: the task may carry the suspected-bug limit; the agent and the guardrails use it.

## Impact

- Code: `src/Maf.Lab.TestGen` (`RunLimits`, `TestGenRequest`, `TestGuardrails`), `src/Maf.Lab.TestAgent`
  (instructions, tools, handler), `src/Maf.Lab.Api` (models and runs endpoints, `TestGenRuns`, `RunFollower`,
  `RunVerifier`, run row), `web/src/coverage` (`ModelPicker`, `limits.ts`), `web/src/api/types.ts`.
- Storage: two nullable columns on the run row, added by the existing additive column pass; old rows read as the
  defaults (the configured deadline and 3).
- No Jev call is added or changed. No package moves. No model changes.

## Documentation impact

- `docs/http-api.md`: the models response's `limits` and the runs body's and summary's `limits`.
- `DECISIONS.md`: a new entry for the two editable limits and their bounds.
- README.md, CLAUDE.md, openspec/project.md and .github/copilot-instructions.md: not affected — they do not describe
  the picker's limits.
