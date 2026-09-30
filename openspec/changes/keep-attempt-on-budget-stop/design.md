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

1. **`run.Attempt = Math.Max(run.Attempt, report.Attempts.Count)`** in `ApplyAsync`. Progress already set the started
   attempt; a report never lowers it. A stop before attempt n + 1 starts leaves n, which is right. Alternative: add a
   `LastAttempt` to the report — rejected, a wire change for a value the api already has.
2. **Backfill in `DatabaseInitializer.BackfillAsync`**: `UPDATE TestGenRuns SET Attempt = (SELECT MAX(Attempt) FROM
   TestGenRunActivity …) WHERE Attempt < that`. Idempotent, runs on every start like the existing backfill; today it
   corrects exactly one row.

## Risks / Trade-offs

- A run whose activity was capped and dropped its oldest entries still has its newest ones, so the maximum is intact.
