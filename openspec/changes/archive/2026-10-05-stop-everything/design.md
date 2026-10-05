# Design

## Context

What can be stopped today, and how (inventory of the code, 2026-10-04):

- **Stops:** the chat run (Esc → CopilotKit stop → the runtime aborts the AG-UI request → `RequestAborted` → MCP →
  Qdrant/Neo4j, all on one token; `esc-stops-chat-run`); test-generation runs (Cancel button → A2A `tasks/cancel` +
  `TestGenRuns` row); A2A partner tasks from the admin screen.
- **Does not stop:** admin jobs (`AdminJobRunner`: index, migrate, coverage refresh) — no route, no `canceled` state,
  the work gets only `ApplicationStopping`; coverage runner jobs — no route, in-memory queue, one replica; the api's
  billing A2A loop — uses only `ApplicationStopping`, and `SqliteTaskStore.SaveTaskAsync` overwrites a terminal state;
  every browser request — `apiRequest` takes a `signal`, no caller passes one; `A2AProbe`, `screenshots`,
  `conformance.mjs` and the scripts that start server work ignore Ctrl+C; `Maf.Lab.Eval` has no top-level cancel.
- **Stores:** every Qdrant and Neo4j call passes the caller's token except `VerifyConnectivityAsync` (two sites).
  Whether the Neo4j driver ends the transaction on the server when the token fires is not yet shown.
- **Esc today:** the chat page (stop), the history rename (cancel edit), the coverage Activity dialog (close only).
- State: admin jobs, test-generation runs and the api's A2A tasks are rows in the api's SQLite on the `api-data`
  volume, shared by both api replicas; the A2A agents keep tasks in Redis (`RedisTaskStore`).

`esc-stops-chat-run` lands first: it brings `TaskCancelWatch` and the atomic terminal guard in `RedisTaskStore`.

## Goals / Non-Goals

**Goals:**
- One rule, applied everywhere: Esc on the page, Ctrl+C in a terminal; one way to stop long-lived work (its own store,
  atomically, watched by the worker); nothing invented on the wire.
- Shared building blocks so a new feature gets the rule by using them, not by re-implementing it.

**Non-Goals:**
- Undoing work already done before the stop (an indexed document stays indexed; a merged candidate stays merged).
- Stopping in the middle of an indivisible step (a git merge, writing one document, one migration batch).
- Moving admin-job or run state out of SQLite.

## Decisions

**A stop is recorded in the store that already owns the work's state, atomically.** One rule, two stores, because the
state already lives in two:
- *Redis* for the A2A agents' tasks — the Lua guard from `esc-stops-chat-run`.
- *The api's SQLite* (shared by both replicas on one volume) for admin jobs, test-generation runs and the api's own
  A2A tasks. The stop is one conditional statement — `UPDATE AdminJobs SET State='canceled' … WHERE Id=@id AND State IN
  ('queued','running')` — and every later state change uses the same guard (`… AND State NOT IN (terminal)`), so a
  late write cannot undo it. `SqliteTaskStore.SaveTaskAsync` gets the same guard on the task's state column.
Alternative — put a cancel flag in Redis beside rows that live in SQLite: rejected, the job's truth would then be split
across two stores with no transaction between them, which is the race this rule exists to remove. Alternative — move
these rows to Redis: rejected here (out of scope; the rows join the api's other tables).

**Workers watch their store.** `TaskCancelWatch` (A2A tasks, from `esc-stops-chat-run`) and a small
`JobCancelWatch` for admin jobs poll the owning row every second and cancel a token linked into the work.
`AdminJobRunner` runs each job under `ApplicationStopping` linked with that token; the job's code already takes a
token (indexing, migration, refresh). `BillingAgentHandler` runs its stages under the task watch. Alternative —
pub/sub: rejected, a second channel to keep consistent for under a second of latency.

**Safe points, not abrupt stops.** Work checks its token between items and shields an indivisible step:
`IndexingPipeline` passes `CancellationToken.None` into `ReplaceDocumentAsync` for the document in hand and checks the
run's token before the next one; migration checks between batches (it is already restartable); the graph build says
"stale nodes not removed — run `make graph` again" when stopped before `RemoveStaleAsync`; `rebuild-index` says the
collection is partial and to run it again. Candidate accept/discard checks the token before the merge and not during
it.

**Admin jobs get `canceled` and a cancel route.** `POST /api/admin/jobs/{id}/cancel` (admin only, firm-scoped like the
job), 202 with the job, 409 when it has already ended. The job ends `canceled` with how far it got. Coverage refresh
cancels the runner job it waits on. Test-generation runs keep their existing cancel route; the Activity dialog maps
Esc to it.

**The coverage runner cancels in-process.** It is one replica by design (no secrets, no egress), its queue is its
state, so its stop is local: `POST /runs/{id}/cancel` cancels the job's token, `ChildProcess` already kills the tree on
cancel, `JobExecutor` already removes the workspace; a joined job keeps a caller count and is cancelled when it reaches
zero; a cancelled result is never stored for reuse. `CoverageRunnerClient.RunAsync` sends the cancel when its token
fires.

**The web: one hook and the signal everywhere.** `useEscToStop({ running, canStop, stop })` holds the window listener
the chat page has today (skips `defaultPrevented` and `isComposing`, one stop per run) and the "Esc to stop" /
"Stopping…" hint component; the chat page moves onto it. `useApi` forwards react-query's `signal` from every
`queryFn`/`mutationFn` (`api(path, { signal })`), so leaving a page aborts its requests; a page's long read registers
its query with the hook so Esc cancels it (`queryClient.cancelQueries`). The api already stops on `RequestAborted`.

**CLI: one pattern per runtime.** .NET tools: `Console.CancelKeyPress` + `PosixSignalRegistration(SIGTERM)` →
cancel the root token, catch the cancellation once at the top, print the final line, exit 130 (Indexing has it; Eval
and A2AProbe get it). Node tools: `process.on('SIGINT'|'SIGTERM')` → close the browser / connections, exit 130. Bash
scripts that start server work: `trap 'cancel_started; exit 130' INT TERM`, where `cancel_started` calls the job's or
run's cancel route for the ids the script recorded. Progress bars already print a cancelled final line
(progress-feedback).

**Stores, proven.** Integration tests against the Testcontainers Neo4j and Qdrant. Found for Neo4j (driver 6.3.0): a
cancelled `ExecuteAsync(ct)` does **not** stop the query — it ran to its end (≈40 s) — and closing the session waits
for it as long. So every graph read (`TenantScopedGraph.ReadAsync`) and maintenance query runs through `GraphStop`:
the query's transaction carries a random `stopId` in its metadata (nothing else), and a cancel ends that transaction
with Neo4j's own `TERMINATE TRANSACTIONS` (a fixed statement, retried for a moment in case the cancel beat the query to
the server); the driver's "terminated" error surfaces as a cancellation. Shown: the call ends in ≈0.2 s and
`SHOW TRANSACTIONS` no longer lists it. The single read method stays the single read method; only how it is executed
changes. For Qdrant: a cancelled call is cancelled on its gRPC channel and ends at once, but gRPC reports it as
`RpcException(Cancelled)`, which no `catch (OperationCanceledException)` sees — found when a stopped index run came out
as a failure. `QdrantFactory` builds the client over its own `QdrantChannel` with one interceptor that turns that, and
only when the call's own token fired, into an `OperationCanceledException`; every Qdrant call goes through it. A Qdrant query cancelled mid-call ends at once
with a cancelled gRPC status. `VerifyConnectivityAsync` gets the caller's token.

## Risks / Trade-offs

- [Esc now stops a test-generation run from the Activity dialog] → only for someone who may cancel it; the hint says
  so; the dialog's close button still just closes it.
- [Polling rows once a second per running job] → a handful of reads per second at this lab's load; configurable.
- [A stop arriving during an indivisible step waits for it] → the page shows "Stopping…" meanwhile; the steps are
  short (one document, one batch, one merge).
- [Aborting every request on leave changes when cached data refreshes] → react-query keeps the last data; only the
  in-flight request is dropped.
