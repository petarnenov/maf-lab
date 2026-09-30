# Design

## Context

- `TestGenerationHandler.ExecuteAsync` runs the whole task (baseline, attempts, report) inside the request that
  created it; the SDK's `A2AServer` drains the handler's `AgentEventQueue`, projects each event onto the task and
  saves it in `RedisTaskStore` (keyspace `testgen`). Everything the loop knows — attempts, best diff, feedback, usage,
  the reporter's sequence — lives only in local variables.
- When the process stops, `ApplicationStopping` cancels the loop and the task is left `working` in Redis. Nothing
  ever calls the handler for it again: the SDK only executes on an incoming `SendMessage`.
- The task's stored history does not contain the request (the SDK appends the user message only on continuation), so
  the request cannot be read back from the task.
- The api (`RunFollower`) already survives its own restarts, follows through the store-backed
  `SubscribeToTaskAsync`, and enforces the run's deadline from its creation time. It needs no change to keep following
  a resumed task, beyond showing the new entry and mapping the new failure code.
- The agent runs as one replica today, but its task store is shared, so the design must hold for two.

## Goals / Non-Goals

**Goals:** resume an interrupted task by itself within about a minute of the agent coming back; keep finished attempts,
the baseline, usage and the activity timeline; never leave a task `working` with nobody running it.

**Non-Goals:** resuming mid-attempt (a model conversation is not checkpointed — the attempt is rerun); counting the
tokens an interrupted attempt spent before the restart (they are lost, see Risks); recovering tasks of the compliance
reviewer; any api-side re-dispatch.

## Decisions

1. **The agent resumes, the api does not re-send.** The task id is what the api's run, lease and activity record are
   keyed on. Sending a new task would need a new id, re-mapping and a second timeline. Resuming the same task in the
   agent keeps every caller unchanged. *Alternative:* api notices a stale task and restarts the run — rejected: loses
   the attempts done and duplicates the run.

2. **A checkpoint per task in Redis, beside the task.** `task:testgen:checkpoint:{id}` holds JSON: the request, and
   from the baseline on `{baselinePct, nextAttempt, attempts, best {pct, diff, bugs}, workingDiff, current, feedback,
   previousDiff, largestInput, largestOutput, usage {input, output}, lastSeq}`. It is written when the task is accepted
   (request only), after the baseline, and after every finished attempt; it expires with the task's 7-day retention
   and is deleted when the task ends. A `TaskCheckpointStore` in the agent owns it. *Alternative:* a file on the
   `/work` volume — rejected: the volume is not the shared store, and a second replica could not read it.

3. **A lease per running task.** `task:testgen:lease:{id}` = the replica's instance id, `SET NX PX 30000`, renewed
   every 10 s with a compare-and-renew script, deleted by compare-and-delete when the task ends or the host stops
   gracefully (so a restart takes over at once rather than after the lease lapses). A fresh task takes its lease
   before anything else in `ExecuteAsync`, so a sweep never races a task being created.

4. **A recovery background service sweeps.** `TaskRecovery` runs at start and every 15 s: it walks the task index for
   `submitted`/`working` tasks, skips those with a live lease, takes the lease with `SET NX`, and hands the task to
   the handler's resume path. With no checkpoint, the task is failed with `interrupted` (after it has been quiet for
   longer than a lease, so a task another replica is just accepting is not touched).

5. **A resumed task writes through the store itself.** No incoming request means no `A2AServer` drain. The resume path
   creates its own `AgentEventQueue` and drains it: for each event it takes the SDK's per-task lock from
   `ChannelEventNotifier`, reads the task, applies `TaskProjection.Apply` (the SDK's own projection), saves it, and
   notifies local subscribers. The same `TaskUpdater` code as a fresh task writes to that queue, so the loop body is
   shared, not copied. Cancel still works: the resumed run registers in the handler's `_running` map, which the SDK's
   cancel path calls.

6. **The loop takes a starting state.** `RunAsync` is split so that a fresh task builds its state (workspace, baseline)
   and a resumed task rebuilds it from the checkpoint: clone at the commit, apply `workingDiff`, restore usage into
   `RunUsage`, restart the reporter's sequence from `max(checkpoint.lastSeq, highest seq among the task's activity
   artifacts)`, and start the loop at `nextAttempt`. The interrupted attempt keeps its number. A `resumed` entry
   (attempt = the attempt resumed at) is recorded first.

7. **`resumed` is a plain entry kind.** It carries only its attempt (the entry's own field), so no new DTO. The api
   projection closes the open step and emits `CUSTOM maf-lab/testgen-resumed` `{attempt}`; the web timeline turns it
   into its existing `notice` item. `interrupted` joins `TestGenFailure` and the api's code mapping.

## Risks / Trade-offs

- [Tokens of the interrupted attempt are not counted] → the checkpoint is written after each attempt, so at most one
  attempt's usage is lost; the budget check uses the recorded usage and the estimate. Documented in the spec delta
  only as "recorded".
- [Two replicas both think a lease lapsed] → takeover is `SET NX`; only one gets it. Renewal is compare-and-set, so a
  replica that lost its lease (paused past 30 s) stops renewing and its run is canceled at the next renewal.
- [The coverage runner is restarting too] → the resumed attempt's runner call fails like any other runner outage
  (`runner_unavailable`); the sweep starts after the host is up, and the runner client already polls.
- [Scratch workspaces of the dead process stay on `/work`] → unchanged from today; a resumed task makes a new one.
- [Deadline] → unchanged: the api's deadline counts from the run's creation, so time lost to the restart is spent.

## Migration Plan

Deploy by `make`. Tasks started before this change have no checkpoint; if one is still `working` after the upgrade it
ends `failed` with `interrupted` rather than hanging. Rollback: the old agent ignores the new keys; they expire.
