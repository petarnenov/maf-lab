# Design

## Context

- The agent reports progress when each attempt starts (`attempt n of N`), and the api stores `run.Attempt = n`.
- When a `BudgetExceededException` stops attempt n mid-way, the handler breaks out before adding it to `attempts`; the
  final report's `Attempts` holds only finished attempts. `ApplyAsync` then sets `run.Attempt = report.Attempts.Count`,
  lowering n to n − 1 (0 for the first attempt).
- Activity rows (`TestGenRunActivity`) carry the attempt each entry belongs to, so a stored run's true last attempt is
  `MAX(Attempt)` over its activity.

## Goals / Non-Goals

**Goals:** a run never shows fewer attempts than it started; stored rows are corrected.

**Non-Goals:** changing the agent's report (its `Attempts` rightly lists finished attempts); changing the stop entry,
which already names the last attempt.

## Decisions

1. **The attempt count only rises, and reads three sources** in `ApplyAsync`: the progress message (the attempt that
   started), the highest attempt among the observed activity entries, and the report's finished attempts;
   `run.Attempt` is the maximum. The activity matters because a follower that first sees the task once it is done
   gets its final status, which carries no progress (found by the api test: the run stayed at 0 with only the
   report's count). A stop before attempt n + 1 starts leaves n, which is right. Alternative: add a `LastAttempt` to
   the report — rejected, a wire change for a value the api already receives.
2. **Backfill in `DatabaseInitializer.BackfillAsync`**: `UPDATE TestGenRuns SET Attempt = (SELECT MAX(Attempt) FROM
   TestGenRunActivity …) WHERE Attempt < that`. Idempotent, runs on every start like the existing backfill; today it
   corrects exactly one row.

## Risks / Trade-offs

- A run whose activity was capped and dropped its oldest entries still has its newest ones, so the maximum is intact.
