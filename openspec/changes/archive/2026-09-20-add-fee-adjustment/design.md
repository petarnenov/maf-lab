# Design

## Context

See proposal.md — Why. What already exists and shapes this:

- **The billing data is read-only and lives in the MCP server.** `BillingSeedStore` (`src/Maf.Lab.Retrieval/Billing/`)
  loads `compose/seed/billing-runs.json` once into an immutable list, is a singleton, and filters every read by
  `principal.FirmId`. The seed is mounted `:ro` and `mcp-retrieval` runs two replicas. There are **no accounts and no
  fees** anywhere — only 26 runs with an `accountCount` integer.
- **The only durable store is in another process.** `Maf.Lab.Api` owns a SQLite database on the `api-data` volume,
  with WAL and a busy timeout because two API replicas share the file, and a schema created by
  `DatabaseInitializer` from the EF model rather than migrations. `Maf.Lab.Retrieval` references neither EF Core nor
  SQLite and has no writable volume.
- **There is no idempotency mechanism in the repo.** The nearest pattern is the unique filtered index on
  `AdminJobRow` — the database, not the code, enforces "at most one".
- **The consultant is built and unused.** `ComplianceConsultant.ReviewAsync` / `AnswerAsync` return
  `ConsultationResult` (verdict, question, timed out, unreachable, failed) and are registered in DI with **no
  production call site**. Two defects in it only matter once something calls it: a timeout on the first call returns
  an **empty** task id (so the review cannot be resumed, though `a2a-client` requires the id be kept), and the
  verdict's `adjustmentId` is taken **from the remote** when present, which makes an untrusted system the source of
  the correlation key.
- **The protocol pieces exist in the pinned SDK.** ModelContextProtocol 2.2.0 carries both elicitation
  (`McpServer.ElicitAsync`) and MRTR (`InputRequiredResult`, `RequestState`, `InputRequest`,
  `McpClient.ResolveInputRequestsAsync`). Nothing in the repo uses either; `DECISIONS.md` §7 records MRTR
  `input_required` for write tools as deliberately not exercised.
- **The chat stream is one-way.** `POST /api/chat` returns SSE; the browser reads and cannot answer mid-stream.
  Unknown event names are dropped silently by the web client's `KNOWN` set.

Two project constraints run through everything below: the tenant comes from the principal and never from an
argument, and no message content reaches a log or an audit record.

## Goals / Non-Goals

**Goals:**

- A write that cannot happen by accident, by a model's enthusiasm, or by text arriving from somewhere else.
- The same guarantee under two replicas and a restart: approving twice charges once.
- Each of the reviewer's five outcomes handled as itself, with the flow ending honestly when no verdict exists.
- The existing read tools, their annotations and their contracts left exactly as they are.

**Non-Goals:**

- No UI. The browser gets a new event it does not yet render; the card, the waiting state and session recovery are
  `add-confirmation-ui`.
- No AG-UI protocol work — the event is added to today's stream, and `add-agui-stream` will carry it over.
- No account browsing: no tool lists or searches accounts. The advisor names one, and the write tool resolves it.
- No general-purpose write framework. One tool, one flow.

## Decisions

### The write tool stays an MCP tool, and MRTR carries the confirmation

The project's convention names MRTR `input_required` at the MCP layer, and the original Day-4 brief says "the
agent's MCP write tool returns resultType input_required". Two mechanisms could serve:

1. **MCP elicitation** — the server calls `ElicitAsync` inside the tool handler and blocks until the client answers.
   Rejected. The call would park inside `ChatTurnRunner.InvokeToolAsync` on one API replica while the only channel
   to the user is that replica's SSE response, and the browser has no way to answer mid-stream. Recovering from a
   lost connection would mean a pending-call registry keyed to a replica — the exact thing the stateless MCP server
   was built to avoid.
2. **MRTR `input_required`** — the tool returns a result asking for input with a `requestState`; the turn ends; a
   later call replays the tool with the answer. Chosen. It fits a stateless server, a load balancer, a one-shot SSE
   stream, and a user who takes a minute to decide — or closes the tab and comes back.

### The proposal is signed, not stored

The tool is called twice and must execute what was proposed the first time, but the MCP server may not keep state
between requests and the two calls may land on different replicas. Options:

1. **A pending-proposals table** in a new store, keyed by proposal id. Rejected: it makes the server stateful in the
   way the spec forbids, and it needs a sweeper for proposals nobody ever confirms.
2. **Trust the arguments on the second call.** Rejected outright: the arguments pass through the model. The account
   and the amount that execute must not be re-derived from anything that has been near a language model.
3. **A signed `requestState`** carrying the proposal — adjustment id, firm, account, amount, reason digest, issued
   time — with an HMAC over it and a short expiry. Chosen. The second call verifies the signature, ignores the
   arguments entirely, and executes what the state says. Nothing needs storing until something is actually applied,
   and a confirmation can land on any replica.

The signing key is configuration, shared by the replicas, and documented the way the card-signing key already is —
symmetric, with the asymmetric version named as what a real deployment would do.

### Only what was applied is durable, and the database enforces "once"

The ledger of applied adjustments is the only thing that must survive. It lives in a small SQLite store in
`Maf.Lab.Retrieval` on its own volume, with the same WAL and busy-timeout treatment the API's store gets, because
two replicas share the file. Uniqueness is a `UNIQUE` index on (firm, adjustment id) — the database refuses the
second insert, and the code turns that refusal into "already applied", returning the first application's identifier
and resulting fee. This is the `AdminJobRow` pattern: let the store enforce the invariant rather than a check that
races.

Alternatives: keeping the ledger in the API's database and having the MCP tool call back into the API (rejected —
the server the API calls would start calling the API, with a second authentication hop for every write); or
moving the write tool into the API (rejected — it breaks the convention and gives the model tools from two
different sources).

The seeded fee stays read-only. An account's current fee is the seed plus its applied adjustments, which keeps the
seed honest as a fixture and makes the ledger the only writable thing in the system.

### The flow runs in the API, in code, not in a workflow engine

The brief asks for Microsoft Agent Framework orchestration. `Microsoft.Agents.AI.Workflows` is not referenced today
and would be a new package for a flow that is one branch (threshold), one bounded loop (at most two questions) and
one wait. Taking it would mean a new dependency, a new failure surface and a second place where control flow lives,
to express in a graph what is a dozen lines of C# in the middle of a turn. Not taken; recorded in `DECISIONS.md`
with what would change the decision — a second sub-agent, a fan-out, or a flow that must survive a process restart
mid-way. The same entry carries the brief's "why one assistant agent": different prompts, permissions, owners or
context budgets are the triggers for splitting an agent, and none applies here.

### Observability goes through the turn trace, not OpenTelemetry

The brief asks for OTel spans carrying the A2A task id. The repo has no OpenTelemetry and no `ActivitySource`; it
has its own turn trace, which the behind-the-scenes monitor already renders and the time-travel view already
replays. Each step of the flow — proposed, review requested, verdict, confirmation requested, applied — becomes a
trace event with the adjustment id and, where one exists, the A2A task id. Introducing OTel to satisfy one
sentence would add a stack nothing else in the lab uses; the gap is recorded rather than half-built.

### The consultant's two defects are fixed here, by its first caller

A timeout must carry the task id, which means capturing the submitted task id before the deadline can fire rather
than reading it from a result that never arrived. And the verdict's identifiers become checks rather than inputs:
the adjustment id and account id that this system sent are the ones it keeps, a verdict naming anything else is a
failed review, and the reviewer starts echoing the account id so the check has something to compare.

### Rejection is a tool result, not an error

When the user declines, the agent is told the user declined, as a normal tool result, and the conversation
continues. An error result would invite the model to retry the write.

## Risks / Trade-offs

- **A second database in the stack** → It holds one table, is created the same way the API's is, and rides the same
  compose/volume pattern; `make doctor` and the topology probe learn about it so it is not invisible.
- **A signed state travels through the model's context** → It is opaque and integrity-protected, so the worst a
  model can do is return it unchanged or corrupt it; a corrupted state is refused. The reason digest, not the
  reason, is inside it, so no free text rides along.
- **A short expiry can strand a user who walks away** → Expiry is configuration, generous by default; an expired
  state is refused with a message that says to propose again, and nothing is half-applied.
- **The reviewer is slow by design and the turn now ends while it is working** → The consultation's deadline is the
  existing one, and every non-verdict outcome ends the flow with an explanation rather than a hang.
- **New selection eval cases move the gate's denominators** → The change ends with a deliberate baseline decision:
  run the suite, read the comparison, and accept it explicitly or fix the tool description until it passes.
- **The web client silently drops unknown events** → `confirmation_required` is added to the client's known set and
  the audit page's kind filter in this change, even though nothing renders the card yet, so the event is not
  invisible until the next change.
