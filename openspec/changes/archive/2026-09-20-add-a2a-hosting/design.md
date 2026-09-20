# Design

## Context

See proposal.md for why. What exists, and what the SDK actually gives — both checked rather than assumed:

- The agent is a MAF `ChatClientAgent` built per turn in `ChatTurnRunner`, with tools from the MCP server, a
  SQLite history provider, tracing middleware and forced-retrieval emulation. Its tools are read-only:
  `search_documents`, `get_billing_run_status`, `search_billing_runs`. Billing runs come from a seeded JSON store
  (`BillingSeedStore`) — there is no engine that *starts* one.
- Identity is a dev-issued JWT with `userId`, `firmId`, `role` claims, one audience, HMAC signing. There is no
  service identity and no scope claim.
- `Audit` is a chained record of actions with `Kind`; two api replicas share one SQLite file; `AdminJobRunner`
  already shows the pattern for cross-replica state.
- **Package check (the request named packages that do not exist):** `Microsoft.Agents.AI.Hosting.A2A(.AspNetCore)`
  is not published. `Microsoft.Agents.*.A2A.Preview` exists but belongs to the M365 Agents SDK, depends on
  `A2A.AspNetCore` anyway, adds an Azure Blob store, and adapts a Bot-Framework turn model this project does not
  use. `A2A` / `A2A.AspNetCore` **1.0.0-preview2** carry what this change needs: `AgentCard` with
  `AgentCardSignature`, extended-card support, `ITaskStore` (+ `InMemoryTaskStore` only), `TaskUpdater`,
  `TaskStatusUpdateEvent`, push-notification configuration, `IAgentHandler`, and both `MapA2A` (JSON-RPC) and
  `MapHttpA2A` (HTTP+JSON).

## Goals / Non-Goals

**Goals:**
- Another agent can discover the assistant and use it without sharing code with it.
- A partner sees only what it is entitled to, decided on the server.
- A long task survives a dropped stream and a replica change.
- Every gap between the preview SDK and the 1.0 specification is written down rather than papered over.

**Non-Goals:**
- gRPC. `A2A.AspNetCore` maps JSON-RPC and HTTP+JSON only; adding a third transport by hand is not this change.
- A real billing engine. Starting a run is a *simulated* long operation over the seeded data — it is the task
  lifecycle that is being built, not a billing product.
- The remote compliance agent, the fee-adjustment write path, the AG-UI stream, the confirmation card and the admin
  screen: each is its own change, and each depends on this one.
- Tiered tenancy at the A2A layer beyond the per-partner allowed-firm check.
- Production webhook security (mutual TLS). A shared token is what the lab uses, and the gap is recorded.

## Decisions

### The protocol SDK, with the MAF agent behind its handler
`A2A.AspNetCore` maps the transports and `IAgentHandler` is where work arrives; our implementation translates an
A2A message into the same `ChatTurnRunner` path a chat turn takes, and translates the result back. Nothing about
the agent, its tools or its tracing changes — a partner's question is answered by the same machinery as a user's,
under a different identity.

Rejected: `Microsoft.Agents.Extensions.A2A.Preview`. It is a bridge to a different agent model (`A2ATurnContext`,
`A2AActivity`), depends on `A2A.AspNetCore` underneath, and would bring a second agent framework and an Azure Blob
dependency into a host that needs neither.

### A partner is a different kind of principal, not a user with extra claims
The dev issuer gains a second kind of token: subject = partner id, audience = the A2A endpoint, a `scope` claim,
and the firms that partner may see. Chat tokens keep their audience, so a chat token presented to A2A fails
validation by construction rather than by a check someone must remember to write.

Entitlements are read server-side from configuration (`A2A:Partners`), never from the token's own claims about
which firms it wants — a token can carry what it likes, but the answer comes from the server's list. This is the
same rule tenancy already follows: the caller never names the tenant.

An out-of-scope request is **rejected**, not "not found" and not empty: the caller learns its request was refused,
and learns nothing about whether the run exists. The reason is a fixed sentence, never the firm's name.

### Task state in the shared SQLite, behind `ITaskStore`
The SDK ships `InMemoryTaskStore`, which two replicas behind a balancer cannot share — a task started on one
replica would be invisible on the other. A `SqliteTaskStore` implements `ITaskStore` over the database the
replicas already share, exactly as admin jobs do. Task history rows are what a resubscribe replays.

Rejected: sticky sessions at the balancer. They would hide the problem for streams and still break `GetTask` after
a replica restarts, and the lab's whole point is that any replica can serve anything.

### "Starting a billing run" is a simulated lifecycle over seeded data
There is no billing engine, and inventing one is not this change. The task walks the states with realistic timing
and ends with an artifact describing the run's final status drawn from the seed store. The simulation is *obvious*
in the code and stated in the card's skill description, so nobody mistakes it for an engine. This is the honest
way to build the lifecycle the protocol cares about without pretending to a capability the lab does not have.

It is the extended card's private skill because it is the shape of thing a partner should not discover casually.

### The card is signed with the lab's existing symmetric key, and that limitation is written down
`AgentCardSignature` is JWS. The lab has one HMAC signing key for dev tokens; the card is signed with it and the
verification procedure is documented. A real deployment would use an asymmetric key so a partner can verify without
holding a secret — that gap goes in `DECISIONS.md` next to the others, rather than being implied.

### Push delivery is at-least-once, recorded, and never fails the task
Each transition enqueues one delivery to the registered webhook with the registered token. A failure is retried a
small fixed number of times and then recorded; the task proceeds regardless. The spec says "exactly one delivery
per transition" as the observable behaviour of a working webhook — the retry path is deliberately visible in the
record so a partner can see what happened.

### Auditing reuses the chain
Inbound A2A requests are appended through `ToolAudit` with a new kind, so partner activity is ordered against tool
calls, deletions and exports in the same tamper-evident record — which is what an investigator asking "what did
this partner do" needs.

## Risks / Trade-offs

- **The SDK is preview.** A breaking change between previews would hit the handler and the card shapes. Mitigated
  by pinning the exact version, keeping our code behind our own thin surface, and recording the version in
  `DECISIONS.md`.
- **A simulated run could be mistaken for a real one** → said plainly in the skill description, in the artifact and
  in the README.
- **A symmetric card signature means the verifier holds a secret** → recorded as a known gap with the asymmetric
  fix named.
- **A long task holds a stream** → the stream is an SSE response like the chat one, and the task itself lives in
  the store, so a dropped stream costs nothing.
- **Another surface that can reach the agent** → it reaches it through the same tool set and the same tenant rules;
  the new surface adds an identity type, not a new path to data.

## Migration Plan

Additive: new endpoints under their own path, new tables created by the existing additive initializer, a new token
kind beside the existing one. Nothing a user or the web app touches changes. Removing the endpoint mapping disables
the whole surface; leftover task rows are inert.

## Open Questions

- Whether the resubscribe replay should be bounded (a task with hundreds of updates) — answerable once the
  simulated run's update count is known, and it does not change the contract, which says "the full current state
  first".
