# Design

## Context

See proposal.md — Why. What already exists and shapes this: `src/Maf.Lab.Api/A2A/` holds the whole server side of
A2A (partner identity and authentication, the card factory, the 1.0 wire translation, a SQLite task store, push
delivery and the billing handler), and the preview SDK's dialect is translated by `SpecWire` in both directions.
`Microsoft.Agents.AI.A2A` is already pinned and already used, by `tools/Maf.Lab.A2AProbe`, to turn a card into an
`AIAgent`. The balancer terminates every path on host port 7171, and every service reports itself through
`X-Instance` and the topology probe.

Two constraints from the project carry straight into this design: the tenant comes from the principal and never
from a parameter, and no message content reaches a log or an audit record.

## Goals / Non-Goals

**Goals:**

- A second agent that is genuinely separate — its own process, its own image, its own identity and its own card —
  so that "the remote agent is down" is a state the lab can actually produce.
- One place for the A2A code both agents need, rather than a copy in each.
- A consultation whose every failure mode is a value the caller can act on: verdict, question, timeout,
  unreachable, error.

**Non-Goals:**

- No orchestration: this change gives the assistant the ability to consult the reviewer and nothing that decides
  when to. The workflow, the write tool and the confirmation step are later changes.
- No new protocol work: the reviewer speaks the same A2A the billing agent already serves, through the same code.
- No persistence of reviews beyond the reviewer's own task store.

## Decisions

### A shared `Maf.Lab.A2A` project, not a copy and not `Maf.Lab.Retrieval`

The reviewer needs partner identity, the card factory, the wire translation and a task store — all of which live
in `Maf.Lab.Api` today. Three options:

1. **Copy into the new service.** Rejected: two copies of a wire-format translation is how the two agents start
   disagreeing about the protocol.
2. **Put it in `Maf.Lab.Retrieval`.** Rejected: that project is referenced by the MCP server and the indexer,
   which would then carry the A2A packages for nothing.
3. **A new `src/Maf.Lab.A2A` class library**, referenced by `Maf.Lab.Api` and `Maf.Lab.ComplianceAgent`. Chosen.
   It takes `PartnerIdentity`, `PartnerAuthentication`, `AgentCardFactory`, `SpecWire`, `SpecWireMiddleware` and
   `A2ARequestHandlerWithExtras`; `BillingAgentHandler`, `AssistantBridge`, `SqliteTaskStore` and
   `PushNotificationDispatcher` stay in the API because they are about *this* agent's work and *this* database.

The card factory becomes card-agnostic: it builds a card from options (name, description, skills, base address)
rather than hard-coding the billing agent's. Each service supplies its own.

### The reviewer's task store is in memory

The reviewer is a stand-in. Its reviews live minutes, not days, and nothing outside it reads them. The SDK's
`InMemoryTaskStore` is therefore enough — but it is per-process, and the tier runs two replicas, so a review is
only visible on the replica that started it. Two ways out:

- **A shared store**, as the API has. Rejected for now: it would mean a database for a service that has no other
  state, to solve a problem the caller does not have.
- **Session affinity for the reviewer's tasks.** Chosen: the balancer routes the reviewer by a hash of the URL
  path, so a task's follow-ups land on the replica that owns it. This is recorded as the limitation it is — if a
  replica dies, its in-flight reviews die with it, which is exactly what a caller's timeout is for.

### The consultation returns a result type, not an exception

`ConsultationResult` is one of: `Verdict` (decision, reason, task id), `QuestionAsked` (question, task id),
`TimedOut` (task id), `Unreachable` (reason), `Failed` (reason). Exceptions are for programming errors, not for a
reviewer that is thinking. The caller — the workflow, in a later change — must handle every case, and a compiler
that enumerates them is better than a `catch` that hopes.

The task id survives a timeout on purpose: the review is still running, and the next change can collect it.

### Discovery through the card, once, cached

`A2ACardResolver.GetAIAgentAsync` fetches the card and builds an `AIAgent`. Fetching it per consultation would
make every review pay for discovery, and caching it for the process's lifetime would survive the reviewer
changing. It is cached for a configurable interval (default a few minutes) and re-fetched after that, or when a
consultation fails at the transport level.

### The assistant's credentials are its own

The assistant holds a client id and secret for the reviewer, from configuration, and exchanges them at the
reviewer's token endpoint. It never forwards a user token — different audience, and a user is not a system. The
reviewer's registration decides what a caller may ask for, exactly as the billing agent's partner registry does.

### `AIAgent` for the call, the A2A client underneath

The consultation uses the Agent Framework's `A2AAgent` (from `Microsoft.Agents.AI.A2A`) rather than raw JSON-RPC:
it is the framework's own answer to "talk to a remote agent", it carries the session (context id and task id)
that resuming a review needs, and it is the same abstraction the assistant's own agent uses. Where it does not
reach — reading the task's final artifact, and telling `input-required` apart from a verdict — the underlying
`IA2AClient` is used directly and the gap is recorded.

## Risks / Trade-offs

- **The reviewer's slowness is the point, and it will make tests slow** → its duration and its
  ask-for-justification rate are configuration, not constants. Tests set them to "instant" and "always" or
  "never"; the stack keeps lifelike values.
- **Two replicas with in-process tasks** → path-hash affinity at the balancer, and a timeout in the caller. A
  review lost with its replica is reported as a timeout, which is the truthful answer.
- **Moving code between projects touches a lot of files without changing behaviour** → the move happens first, as
  its own task, with the existing suite as the check that nothing changed.
- **A cached card outlives a reviewer that moved** → the cache has a lifetime and is dropped when a consultation
  fails at the transport level.
- **The reviewer is another surface to secure** → it is behind the same partner authentication, with its own
  audience, and a token for the billing agent's audience is refused. A test asserts exactly that.

## Migration Plan

Additive throughout: a new project, a new service, new configuration. The existing A2A surface keeps its paths and
its behaviour; the shared-project move is a compile-time refactor with no wire change. Rolling back is removing
the compose service and the assistant's configuration — the assistant treats a missing reviewer as `Unreachable`,
which it already has to handle.
