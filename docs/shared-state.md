# The shared state store

Stateless does not mean without state; it means the state is in one place every replica can see. This system
mostly believed that already — what it did not is what this describes.

## Where each thing lives, and why

| State | Where | Why there |
|---|---|---|
| A run while it runs | Redis | The tab closes and the client comes back on another replica |
| Idempotency keys a caller owns | Redis | The write happens in the MCP server; the record belongs beside it |
| The reviewer's A2A tasks and webhooks | Redis | Two replicas, no database of its own, and no page that queries them |
| The test agent's A2A tasks, their checkpoints and leases | Redis | A run outlives the process running it; after a restart the next one takes it over |
| Conversations, messages, what one is waiting on | SQLite | Already shared; the list is one query over the turns, which do not move |
| The assistant's A2A tasks and webhooks | SQLite | `/admin/a2a` reads them in one query with the audit rows |
| Turns, traces, audit chain, feedback, labels, admin jobs | SQLite | Written to be read later, not shared in flight |
| Applied adjustments | SQLite (the MCP server's own) | The record of the money, with its `UNIQUE (firm, adjustmentId)` |

A handle this system hands out is self-describing or it is in a store: a history cursor is `{ticks}:{id}`, a
proposal's `state` is a signed payload. A handle that was a key into one replica's memory would be a session
under another name, and the next request reaches a different replica.

## What a replica does without it

A service that needs the store does not start without one — `RequireSharedState<T>()` says which. A replica that
started anyway would answer some requests correctly and lose others, which is worse than not being in the pool.

While running, `/health` returns 503 for as long as the store cannot be reached, so the balancer routes around the
replica; it serves again when the store answers, without a restart.

To look at what is in the store — keys, their TTLs, the JSON in them, live commands — open Redis Insight on
http://localhost:7174, which `make` starts already connected to it.

## Configuration

| Setting | Meaning | Default |
|---|---|---|
| `SharedState:ConnectionString` | Where the store is. Unset means this service keeps nothing shared | — |
| `SharedState:RunGrace` | How long a run's state outlives the run | 1 hour |
| `SharedState:IdempotencyWindow` | How long a processed key is remembered | 24 hours |
| `MessageRetention:RetentionDays` | How long message content is kept, wherever it is kept | 90 days |

`MessageRetention:RetentionDays` is the compliance retention and is deliberately not `Tracing:RetentionDays`: one
says how long what was said is kept, the other how long the working of a turn is kept, and they move apart.

## Rejoining a run

A run's snapshot (`run:{runId}`) holds where the run stands: the answer so far, the tool calls and how each ended,
whether it is waiting for a person, and how it finished. A client rejoins through the protocol itself — a run on the
same thread naming the lost run as its parent — and any replica can answer it from the snapshot, because the state is
not any one replica's. It is a snapshot and not a replay: the client is told where the turn stands, as the protocol's
events, not the frames it missed. While a run is going its trace is kept beside it (`runtrace:{runId}`, one list entry
per event, for the same grace period) so the monitor can follow it from any replica. See [http-api.md](http-api.md).

## A test run across an agent restart

The test agent runs a task in the process that accepted it, but keeps what the task needs beside it in the store:
`task:testgen:checkpoint:{id}` holds the request and, after the baseline and after every finished attempt, where the
loop stood (attempts so far, the best result and its diff, the tests written so far, the feedback, usage, the last
activity number). `task:testgen:lease:{id}` names the replica running it; it lasts `TestAgent:LeaseFor` (30 s) and is
renewed every third of that, and given up when the host stops.

On start, and every `TestAgent:RecoverEvery` (15 s), the agent takes over each `submitted` or `working` task whose lease
has lapsed and resumes it from its checkpoint: an interrupted attempt runs again from the tests the last finished one
left, usage and activity numbering go on, and a `resumed` entry tells the page. A task with no checkpoint ends
`failed` with reason `interrupted`. Both keys go when the task ends.

## An idempotency key

A caller sends its own key with a confirmation; the server answers a repeat with the first answer rather than
doing the work again, and refuses a different request under a used key. The key travels in the request's
metadata (`maf-lab/idempotencyKey`) rather than in a tool argument, so a model never invents one.

The ledger's `UNIQUE (firm, adjustmentId)` is untouched and remains the guarantee about the money. The key is the
guarantee about the *call*: it is what makes sending an interrupted call again safe.
