# Design

## Context

See proposal.md — Why. What shapes the approach:

- **The shared store already exists; it is a file.** `MafDbContext` over SQLite on the `api-data` volume, in WAL
  with a busy timeout so two api replicas wait for each other's writes (`SqlitePragmaInterceptor`). The MCP
  server has its own, `maf-lab-adjustments.db` on `retrieval-data`, for the applied ledger. `DatabaseInitializer`
  creates tables and adds columns on start.
- **What is genuinely per-replica.** `RunRegistry` (a `ConcurrentDictionary` of cancellation sources, per replica
  by design, with `RunStopper` asking the other replicas when a stop lands in the wrong place), and the
  compliance agent's `InMemoryTaskStore` and `InMemoryPushConfigStore` — which it runs two replicas of.
- **The interfaces are already there for two of the six.** The A2A SDK defines `ITaskStore` and this repo defines
  `IPushConfigStore`; `SqliteTaskStore` and `SqlitePushConfigStore` implement them. A Redis implementation is a
  sibling class, not a change to a caller.
- **Handles are already right.** A history cursor is `{ticks}:{id}`, parsed anywhere. A proposal's `state` is a
  signed `FeeAdjustmentProposalState` that `ProposalSigner` checks. Neither is a key into memory.
- **At-most-once already holds for the money.** `FeeAdjustmentLedger` has a `UNIQUE (firm, adjustmentId)` index,
  and the refused insert is the answer "already applied" rather than an error.
- **One loop sees every event of a run.** `ChatEndpoints.Stream` yields each `BaseEvent` to the wire and, since
  the AG-UI frame log, records each one on the way past. That is where a run's shareable state can be kept
  up to date without threading a new dependency through the runner.

## Goals / Non-Goals

**Goals:**

- One place every replica reads, for everything a replica must not own alone.
- A run that can be rejoined from any replica, because the browser closes tabs.
- A write a caller can retry safely in its own terms.
- Interfaces that make Postgres a second implementation rather than a second migration.

**Non-Goals:**

- Moving the audit chain, the turns, their traces, the feedback, the labels or the applied ledger. They are
  written to be read later, not shared in flight, and Redis is not where a thing you must prove belongs.
- Replaying the frames a client missed. A rejoined run gets a snapshot (see proposal.md — Assumptions).
- Postgres, clustering, Redis Sentinel or a failover story. The lab runs one Redis.

## Decisions

### One store, reached through one interface per kind of state

`Maf.Lab.Hosting` gains `AddSharedState(...)`, beside `AddLabTelemetry`: it reads the connection string, builds
one `IConnectionMultiplexer` for the process, and registers the stores. Each kind of state gets its own narrow
interface — `IConversationStore`, `IRunStateStore`, `IIdempotencyStore` — alongside the A2A SDK's `ITaskStore`
and this repo's `IPushConfigStore`. Callers depend on those, never on Redis.

*Alternative:* one `ISharedState` with a key-value shape. Rejected — every caller would then know about keys and
serialization, and the Postgres change would touch every caller instead of five classes.

**StackExchange.Redis 3.3.0**, `redis:8.8.3-alpine` in compose, both pinned with a DECISIONS.md note as every
version here is.

### Keys, and what each one is for

| Key | Holds | Kept for |
|---|---|---|
| `conv:{firmId}:{conversationId}` | the conversation and its messages | the compliance retention |
| `conv:index:{firmId}:{userId}` | the caller's conversations, ordered by last activity | with the conversation |
| `pending:{conversationId}` | what the conversation is waiting on | until answered or expired |
| `run:{runId}` | the run's snapshot: answer so far, tool calls, waiting, outcome | the run, then a grace period |
| `task:{agent}:{taskId}` and `push:{agent}:{taskId}` | A2A tasks and their webhooks, per agent | the task's own lifetime |
| `idem:{firmId}:{key}` | the first answer given under that idempotency key | 24 hours |

Every key is prefixed by the firm where the thing belongs to one, so a mistake in a query cannot cross a tenant
boundary any more than the SQLite filter could.

### The run's snapshot is written where the frames already pass

`ChatEndpoints.Stream` updates `run:{runId}` as it yields: the text as it grows, a tool call when it starts and
when it ends, the interrupt when a run pauses, and the outcome when it finishes. It is one writer per run, so
there is no contention, and the snapshot is exactly what the client would have seen.

`GET /api/chat/{runId}` returns it, beside the `POST /api/chat/{runId}/stop` that is already there. The thread's
ownership decides who may read it, as it does for the run itself.

`RunRegistry` stays for what it actually is — the cancellation of a request this replica is serving — which the
shared-state requirement explicitly allows. `RunStopper` keeps asking the other replicas, because a stop has to
reach the process holding the token and a flag in Redis would have to be polled.

### The idempotency key is the caller's, and lives where the write happens

The browser generates a key when it confirms a write and sends it with the resume; the api passes it to the MCP
tool; the MCP server records `idem:{firm}:{key}` → the answer it gave, and replays that answer for a repeat. The
key is a tool argument rather than a model-visible invention: on the confirm path the api fills it, and the model
never proposes one.

This is the layer that makes "the answer never arrived" safe. The ledger's `UNIQUE (firm, adjustmentId)` stays
exactly as it is and remains the guarantee about the money; the key is the guarantee about the *call*.

A second, different request under a used key is refused — the stored answer records a digest of the request it
answered, so a key reused for something else is caught rather than silently answered with the wrong result.

### Refusing to start is a feature

A service that needs the shared store and cannot reach it does not begin serving. A replica that starts anyway
would answer some requests correctly and lose others, which is worse than not being in the pool: the balancer
already routes around a replica that is not healthy. While running, the health endpoint reports unhealthy for as
long as the store is unreachable, and recovers without a restart.

### Existing conversations are copied once, not abandoned

On first start against an empty shared store, the api copies the conversations, messages and pending proposals it
finds in SQLite. It is a few dozen lines and it is the difference between a deployment that keeps its history and
one that silently loses it. The SQLite tables are left in place, unread, so the copy can be repeated if it has to
be.

### Redis is a service of the stack, so it appears as one

It joins `docker-compose.yml`, the topology report and `docs/topology.drawio` with its edges, as the telemetry
services did. A test already refuses a diagram whose nodes do not match the report.

## Risks / Trade-offs

- **Message content moves to Redis, which is not an audit store** → it is what was asked for as the first phase,
  and the proposal records it: what must be provable stays in SQLite, and auditability of the state itself is
  what the Postgres change is for. Redis persists with its own snapshot on a volume, which is durability, not
  evidence.
- **A store everything depends on is a single point of failure** → that is true of the SQLite file today as well;
  the difference is that the failure is now loud (refuse to start, report unhealthy) instead of quiet.
- **Two stores mean two retentions to keep straight** → the spec requires them to be independent settings and a
  test holds that changing one does not change the other.
- **A snapshot is not a replay** → a client that was away sees where the turn stands, not the frames it missed.
  The AG-UI frame log already numbers frames, so a later change can add replay without changing this shape.
- **The MCP server now needs the shared store too** → for the idempotency record, which belongs where the write
  happens. It already shares a volume with its replicas; this replaces that dependency rather than adding one.

## Migration Plan

Bring Redis up first; the services refuse to start without it. The api copies its conversations across on first
start. Rollback is putting the previous images back: the SQLite tables were never dropped, and the copy is
idempotent, so a second attempt costs nothing.

## Open Questions

- What the compliance retention should actually be for a lab. A default is enough to start and changes nothing in
  the specs or the task breakdown.
