# Design

## Context

- `show-run-limits-in-picker` made attempts, tool rounds and test runs per-run limits (`RunLimits`, carried by the
  start request and the task). The deadline (`TestAgentOptions.RunDeadline` in the api, 2 h) and the suspected-bug cap
  (`SuspectedBug.MaxPerRun` = 3) stayed fixed and are shown read-only.
- The deadline is enforced only by the api (`RunFollower` compares `run.CreatedAt` with `RunDeadline`); the agent
  never sees it. The bug cap is enforced by the agent (`TestAgentTools.ReportSuspectedBug`, `TestGuardrails.Check` in
  the handler) and again by the api (`RunVerifier` calls `TestGuardrails.Check`).
- New nullable columns are added by `DatabaseInitializer.AddMissingColumnsAsync`.

## Goals / Non-Goals

**Goals:** both limits editable per run, filled with their defaults, validated in the browser, the api and (for the
bug cap) the agent, and enforced where they are enforced today.

**Non-Goals:** raising either above today's value (default = maximum, as for the other limits); making the target
editable here (it is the threshold the dialog raises); sending the deadline to the agent (the api owns it).

## Decisions

1. **Suspected bugs in `RunLimits`.** `RunLimits.SuspectedBugs = LimitBounds(0, 3, 3)`; `SuspectedBug.MaxPerRun`
   stays as its maximum. `TestGenRequest` gains optional `int? MaxSuspectedBugs` (resolved `SuspectedBugLimit`,
   checked in `Problem()`). `TestGuardrails.Check` gains `int maxBugs = SuspectedBug.MaxPerRun`; `TooManyBugs` stops
   naming 3 ("is one suspected bug too many for this run"). The agent's tool, its description and the instructions
   use the task's limit; the static system prompt's "At most three" moves into the attempt input, which knows the task.
2. **Deadline bounds from configuration.** `TestGenRuns.DeadlineBounds(options)`: max = the configured deadline in
   whole minutes (at least 1), min = min(10, max), default = max. Kept out of `RunLimits` because the api's
   configuration owns it (tests shorten it to seconds).
3. **Stored as given.** The run row gains `DeadlineMinutes` and `MaxSuspectedBugs` (nullable). The api stores a
   deadline only when the start request names one, so a run without one keeps using the configured `RunDeadline`
   exactly (including sub-minute test values); `RunFollower` uses `row.DeadlineMinutes` when set. The bug limit is
   stored resolved. `RunVerifier` checks guardrails with the row's limit (null → 3). `RunSummary.Limits` gains both; a
   null deadline there means the configured one.
4. **Wire shapes.** `RunLimitsDto.DeadlineMinutes` and `MaxSuspectedBugs` become `LimitBounds`; `RunLimitsInput` and
   `RunLimitsSummary` gain `DeadlineMinutes` and `MaxSuspectedBugs`. The web `LimitKey` set grows by two, so the
   picker renders them with the existing field and validation; the read-only list keeps only the target.

## Risks / Trade-offs

- **A short deadline cuts a working attempt.** Accepted: it is the administrator's choice, the minimum is 10 minutes,
  and the run fails with reason `deadline` as today.
- **Wire change.** Additive and optional on the task; an agent that ignores `maxSuspectedBugs` applies 3, the default.
