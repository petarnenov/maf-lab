# Proposal

## Why

A run whose budget is spent during its first attempt ends showing attempt 0, although it spent the tokens of attempt 1
(`r_631320cb…`: 124k tokens, attempt 0). The api overwrites the attempt count with the number of finished attempts in
the agent's final report, which is 0 when the budget stops attempt 1 before it finishes. The file view and the tree
then read "baseline" or "0/5" for a run that did work.

## What Changes

- When the api applies the agent's final report, the run's attempt count stays at the last attempt that started (the
  one progress already reported) and is raised, never lowered, by the report's finished attempts.
- A one-off, idempotent backfill corrects stored runs whose attempt count is below the highest attempt in their
  recorded activity.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `test-generation-runs`: the run's attempt count is the last attempt that started, including one the budget stopped.

## Impact

- Code: `src/Maf.Lab.Api/Coverage/TestGenRuns.cs` (`ApplyAsync`), `src/Maf.Lab.Api/Storage/DatabaseInitializer.cs`
  (backfill). Tests in `tests/Maf.Lab.Tests`.
- No wire, endpoint, Jev, package or model change.

## Documentation impact

None affected: `docs/http-api.md` describes the run's `attempt` without saying how a budget stop counts, and README.md,
CLAUDE.md, openspec/project.md and .github/copilot-instructions.md do not describe it.
