# Design

## Context

See proposal.md (Why) for the run that looked stuck. The parts of the current code that shape the approach:

- **Agent loop.** `TestGenerationHandler` runs the attempt loop. Before each attempt it asks `RunUsage.WouldExceed`
  and breaks with `StopReason.Budget` without reporting anything. `BudgetedChatClient` throws
  `BudgetExceededException` mid-attempt. `RunUsage` compares against `TestGenBudget(long MaxTokens, double MaxCostUsd)`,
  and `TestGenRequest.Validate` rejects non-positive caps.
- **Tool rounds.** The rounds are capped by `FunctionInvokingChatClient.MaximumIterationsPerRequest`
  (`TestAgentOptions.MaxToolRoundsPerAttempt = 12`). Each attempt builds a fresh `ChatClientAgent` with no session.
- **Api.** `TestGenRuns.StartAsync` builds the budget from `TestAgent:Budget` in `appsettings.json` (400 000 tokens /
  $2.00). `RunVerifier` ends an empty diff with `FinishAsync(CompletedNoChange, reason: null)`.
- **Stream projection.** `RunActivityProjection` maps activity rows to AG-UI events. It closes an open step only in
  `Ended`, and it never names the stop reason.
- **Web.**
  - `useRunStream(runId)` opens its own `fetch` stream per caller.
  - The tree row badge (`CoverageTree.tsx`) comes from the tree query and is static until the tree is invalidated.
  - The tree already carries each file's active run, or its most recent run.
  - The LB is nginx on HTTP/1.1, so the browser allows about 6 connections to it at a time.

## Goals / Non-Goals

**Goals:**
- One source of truth for why a run stopped: the agent's `stopped` entry, copied into the run's reason.
- The budget is chosen per run, unlimited by default, and enforced only where it is set.
- The tree row shows a live run without opening more streams than the browser allows.

**Non-Goals:**
- Changing the attempt cap (stays 5), the tool-round cap (stays 12) or the run deadline.
- Carrying one attempt's conversation into the next (a session across attempts). The "wrote nothing" feedback
  targets the observed stall more cheaply. Carrying the session is a separate change if the stall persists.
- Picking a different default model.

## Decisions

### D1. Optional caps as nullable fields, not a sentinel
`TestGenBudget(long? MaxTokens, double? MaxCostUsd)`. When a field is `null`, that dimension is unlimited. `RunUsage`
compares only against the caps that are set. A request with both caps `null` is valid. A cap that is set must be `> 0`.
- *Alternative: `long.MaxValue` / `double.PositiveInfinity`.* This reads as a real cap in logs and JSON, and infinity
  does not serialize in `System.Text.Json` by default. Rejected.
- *Alternative: keep a config default and let the admin lower it.* The user asked for unlimited as the default.
  Rejected.

The api removes `TestAgent:Budget` from `appsettings.json` and the options class. `GET /api/coverage/models` drops
`maxTokens`/`maxCostUsd`. `POST /api/coverage/runs` takes an optional `budget: { maxTokens?, maxCostUsd? }`, validates
it (`400` validation problem naming `budget`, like the other invalid start fields), and persists it in two nullable columns `BudgetTokens` and `BudgetCostUsd` on
`TestGenRuns`. The existing additive pass in `DatabaseInitializer` adds these columns; old rows read as unlimited.
`RunSummary` gains `budget: { maxTokens, maxCostUsd }` with nulls.

### D2. The agent reports its stop; the api copies it
A new `ActivityType.Stopped` entry `{ reason, lastAttempt, bestPct, notStarted? }` is written just before the report
artifact, on every path that completes the task:
- target reached;
- the loop ran out of attempts;
- the pre-attempt budget check;
- `BudgetExceededException` mid-attempt.

Failed and canceled tasks do not get it, because they already end with `RUN_ERROR` and its reason. When the task
completes, the api sets `Reason = report.StopReason` as it moves the run to `verifying`. `RunVerifier` keeps that
reason for `completed_no_change` and `candidate`. It overwrites the reason only when verification fails.
- *Alternative: derive the text in the browser from `report.stopReason`.* The stream is the only channel for run
  data (spec), and a separate report fetch would duplicate the source of truth. Rejected.

`RunActivityProjection` maps a `stopped` row to `STEP_FINISHED` (the open step) and then
`CUSTOM maf-lab/testgen-stopped`. The browser's reducer adds a closing timeline item. `format.ts` holds the
user-facing words for each reason (`STOP_LABELS`), and `RunStatus` and the Activity header use them.

### D3. Round awareness through a delegating client, not the prompt alone
- `Instructions.Attempt` states the round cap.
- A small `RoundNudgeChatClient` sits between `FunctionInvokingChatClient` and `BudgetedChatClient`. It counts the
  model calls in the current attempt (reset by `tools.BeginAttempt()`). On the call where `cap - used == 3`, it
  appends one user message: "3 tool rounds remain; write or improve a test file now."
- `Instructions.Feedback` gains a line when the attempt's diff added or changed no test file, using the existing
  `TestsIn(diff)`.
- *Alternative: raise the cap to 20.* That costs more and does not change the behaviour of a model that keeps
  reading. It is not ruled out later, but it is out of scope here.
- *Alternative: `ChatOptions` tool-choice "required" near the end.* Not every provider supports it, and it forces a
  tool call, not a write. Rejected.

### D4. One shared stream per run in the browser
`useRunStream` becomes a subscriber of a module-level registry keyed by run id: one `fetch` stream per run, reference
counted, and closed when the last subscriber leaves. The reduced state is kept so that a late subscriber gets it at
once. The file view, the Activity modal and the tree row then share one connection per run.

Tree rows open live streams for at most **3** active runs, the ones updated most recently. Any other active row shows
the static badge from the tree query, which then refetches every 10 s while any run is active.
- *Alternative: a multiplexed "all runs" stream.* That is a new endpoint and protocol surface for a rare case (many
  concurrent runs). Rejected for now.

The last-outcome label renders when `run` is final, is not a candidate, and `run.updatedAt > file.measuredAt`. The
tree already returns the most recent run, so no api change is needed for it.

### D5. "Reach it": the confirmation opens on coverage, not on the direction of the change
`ThresholdControl` opens the confirmation whenever the value to save is above `summary.pct`. That includes "Use
default" when the default is above coverage. It no longer compares the value with the stored threshold. The
confirmation's first step gains **Save without a run**, which does the plain `PUT`. On the server, an accepted start
whose `pct` equals the configured default clears the override instead of storing one of the same value, so the
file follows the default if it later changes. The server's `PUT /thresholds` is unchanged: it never starts a run.
- *Alternative: a separate "Reach threshold" button, leaving Save as it was.* The user reads Save on a file below its
  threshold as "reach it", and a second button would keep the surprise. Rejected.

## Risks / Trade-offs

- [Unlimited runs can cost a lot with an expensive model] → The attempt cap (5) and the run deadline still bound the
  run. The picker says "unlimited" plainly and shows the estimate. The cost stays visible live in the header.
- [The nudge message is model-facing text inside the conversation] → It is a fixed string with no source or
  content, so the injection surface does not change. It is not logged.
- [The stream registry leaks a connection if a subscriber never unsubscribes] → Unsubscribing happens in the
  `useEffect` cleanup, and a unit test covers mount, unmount and remount.
- [Old runs have no `stopped` entry] → The timeline shows no closing line for them, and the header falls back to
  `run.reason`, which is null for old no-change runs. That is acceptable.
- [An admin who only meant to lower a threshold now sees a dialog] → "Save without a run" is its first action, and a
  change down to what coverage already meets still saves at once.
- [**BREAKING** request contract between api and agent] → Both are deployed together from one commit. The agent
  accepts both the old shape (numbers) and the new one (nulls), so a rolling order does not matter.

## Migration Plan

1. Agent and contracts first: nullable caps, the `stopped` entry, the nudge. The agent still accepts old requests
   that carry numbers.
2. Api: the columns (additive), the start request, the reason carry-through, the projection. Remove the `Budget` config.
3. Web: the budget fields, labels, the shared stream and the tree marker.
4. `make docs`, then `make docs-check` for the `docs/http-api.md` rows.

Rollback: revert the commit. The new nullable columns are ignored by the old code.
