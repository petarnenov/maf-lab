# Proposal

## Why

Stateless does not mean without state; it means the state is in one place every replica can see. This system
mostly believes that already — conversations, the api's A2A tasks, its webhook registrations and the applied
adjustments are in a SQLite file the replicas share, cursors are self-describing and a proposal's `state` is a
signed payload rather than a key into somebody's memory. What is left is the part that is not:

- **A run has no shareable state.** `RunRegistry` is a `ConcurrentDictionary` of cancellation sources, per replica
  and deliberately so. Close the tab and come back and there is nothing to come back to: no replica can say what
  the turn has done so far, because only the one that is streaming knows.
- **The compliance reviewer runs two replicas with `InMemoryTaskStore` and `InMemoryPushConfigStore`.** A
  `GetTask` that lands on the other replica finds nothing, and a webhook registered on one is unknown to the
  other. The billing assistant solved this with a shared store; the second agent never did.
- **A write has no client-supplied idempotency key.** The ledger refuses a second application of the same
  *adjustment id*, which the server minted. The protocol says an interrupted stream is sent again, and the caller
  that resends has no way to say "this is the same attempt" — only the server's own identifier saves it, and only
  because that identifier happens to be inside the payload.

And the shared store is a SQLite file on a Docker volume. It works for a lab on one host and stops working the
moment the replicas are not on it.

## What Changes

- Redis becomes the shared state store for what is not shared today: live run state, A2A tasks and webhook
  registrations for both agents, and the record of processed idempotency keys.
- Conversations stay in SQLite. They are already outside the process and already read by every replica, and the
  list that shows them is one query over the turns beside them — a query that would become a cross-store join
  for nothing. What they gain here is the compliance retention they never had.
- **A run can be rejoined.** While a turn is running, its state is kept where every replica can read it: what the
  turn has said so far, its tool calls and their outcomes, whether it is waiting for a person, and when it ended.
  A client that comes back — on any replica — asks for the run and is given that snapshot.
- **A write accepts an idempotency key from its caller.** A tool that changes something takes a key the client
  generated; the server remembers the keys it has processed and answers a repeat with the first answer rather
  than doing the work again. The key is remembered for as long as a client could reasonably retry.
- **The compliance reviewer gets the shared store for its tasks and webhooks**, so they survive the replica that
  created them, as the billing assistant's already do. The assistant's stay where they are: `/admin/a2a` reads
  them in one query together with the audit rows, which do not move, and splitting the two would turn that query
  into a join written by hand over an index maintained by hand.
- **A server-minted handle is self-describing or it lives in the shared store.** Cursors and proposal states
  already are; the rule is written down so the next handle is not a key into one replica's memory.
- **BREAKING for a deployment**: the api and the compliance agent need Redis. Without it they do not start, because
  a replica that cannot see the shared state cannot serve correctly and should say so rather than seem to work.
- SQLite keeps what is written to be read later rather than shared in flight, and what is already shared well
  enough: conversations and their messages, turns and their traces, the audit chain, feedback and labels, admin
  jobs, and the applied-adjustments ledger.
- Message content gets one retention wherever it is kept — the conversation, its messages and its turns go
  together — stated separately from the trace retention and from the logs.

## Capabilities

### New Capabilities

- `shared-state`: what state every replica must see, where it lives, what may never live in a replica's memory,
  and what happens when the shared store cannot be reached.

### Modified Capabilities

- `load-balancing`: the cross-replica requirement names the whole list, including run state, tasks, webhooks,
  handles and idempotency keys.
- `agui-stream`: a run can be rejoined from any replica, and reports what it has done so far.
- `fee-adjustment`: at-most-once gains the client's own idempotency key beside the server's adjustment id.
- `compliance-review`: the reviewer's tasks and webhooks survive the replica that made them.
- `chat-history`: a conversation is kept for a stated compliance retention, independent of the trace's.

## Impact

- `compose/docker-compose.yml` — a Redis service; `Directory.Packages.props`, `DECISIONS.md` — the client library.
- `src/Maf.Lab.Hosting/` — one place that wires the shared store, beside the telemetry wiring.
- `src/Maf.Lab.Api/Storage/` — a retention sweep for message content, beside the trace's.
- `src/Maf.Lab.ComplianceAgent/` — the reviewer's tasks and webhooks, in the shared store rather than its memory.
- `src/Maf.Lab.Api/Agent/Streaming/RunRegistry.cs` — run state joins it, and a stop no longer has to ask around.
- `src/Maf.Lab.ComplianceAgent/InMemoryPushConfigStore.cs`, `Program.cs` — the reviewer stops keeping its own.
- `src/Maf.Lab.Retrieval/Tools/FeeAdjustmentTools.cs` — the idempotency key on the write tool.
- `src/Maf.Lab.Api/Endpoints/` — rejoining a run; `docs/http-api.md`, `docs/` — the new shape.
- `src/Maf.Lab.Api/Topology/` and `docs/topology.drawio` — Redis is a service of the stack like any other.

## Assumptions

- **Postgres is the next change, not this one.** The store is written behind an interface per kind of state, so a
  Postgres implementation is a second class rather than an edit to every caller.
- **Redis is not an audit store, and this change does not pretend otherwise.** What has to be provable stays in
  SQLite, and auditability of the state itself is what the Postgres change is for.
- **Conversations were left where they are, deliberately.** The implementation showed that the conversation list
  is a query over the turns, which do not move; splitting the two would have made one query into a cross-store
  join and put the same message text in two places. They are already shared; what they were missing is a
  retention, and that is what they get.
- **Rejoining gives a snapshot, not a replay.** A client that comes back is told where the turn stands, not every
  frame it missed. Frame-by-frame replay would need the run's whole event log kept and cursored, which is a
  larger thing and was not asked for.
