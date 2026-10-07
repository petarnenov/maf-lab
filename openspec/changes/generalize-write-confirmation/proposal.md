# Proposal

## Why

Asking a person before a write happens is a core capability, but today the core only knows one write: the fee
adjustment. `ConfirmationService`, `ConfirmationSink`, `MafDbContext` (`PendingAdjustments`), `HistoryEndpoints`
(`/pending`), `ChatTurnRunner` (`TurnResult.Proposal`, `ProposedAsync`), `RunRejoin`, `ToolSource`, `Program`,
`MessageRetentionService` and `AuditChain` all name `FeeAdjustmentSummary`, `FeeAdjustmentTool` or
`PendingAdjustmentStatus`. `FeeAdjustmentFlow` (the reviewer, the guard, the audit, the trace, the person's question)
lives in the api. The web's `ConfirmationCard` renders fixed fee fields.

That blocks the plugin rollout in two ways. Billing cannot become a plugin without leaving its write flow behind in
the core. And no other plugin can ask a person before it writes without adding another branch to the core.

This change comes right after extract-billing in the confirmed order. It builds on the reviewer-consultation port
that extract-billing part 3 adds to `Maf.Lab.Plugins.Abstractions`, and it does not redefine that port.

## What Changes

- **New capability `write-confirmation` (server).**
  - A write tool's first call returns the protocol's request for input (MCP elicitation), carrying a summary and an
    opaque state. It never returns a change.
  - The core keeps every proposal in one pending-writes store. It confirms, rejects and expires a proposal, and it
    replays a waiting proposal when a run is rejoined. It does this for any write tool, by name, without knowing the
    domain.
- **A Strategy per write tool.** `Maf.Lab.Plugins.Abstractions` gains `IWriteConfirmationFlow`, keyed by the tool's
  name and contributed by a plugin (`IContributesWriteConfirmation`).
  - The core picks the flow by the tool that asked; the flow decides between asking the person and telling the model
    why not.
  - The flow reaches the core only through ports: the reviewer consultation (extract-billing part 3), and three new
    ones in this change: `IWriteAudit` (the audit record), `IPromptScreening` (screening text a person types) and
    `IWriteTraceStep` (a step in the turn's trace).
- **Billing implements the seam.** `FeeAdjustmentFlow` becomes billing's `IWriteConfirmationFlow` for
  `propose_fee_adjustment`. It is written against the ports and no longer takes `ComplianceConsultant`, `Guardrail`,
  `ToolAudit`, `IDbContextFactory<MafDbContext>`, `TurnTrace` or `PersonQuestion` directly. The fee rules themselves
  (review threshold, two questions at most, the verdict check, applied at most once) are unchanged.
- **One store, rows kept.** The table `PendingAdjustments` becomes `PendingWrites`. The existing rows are kept:
  - the table is renamed in place;
  - `awaiting_justification` becomes `awaiting_input`;
  - the fee-only columns (`ReviewTaskId`, `Questions`) move into a flow-owned JSON column (`FlowJson`).
  How it is migrated is open question 1 below.
- **A summary the core does not read.** `CapturedConfirmation` carries the summary as a `JsonElement`, plus the JSON
  Schema the flow describes it with. `TurnResult.Proposal` becomes a `PendingWrite` (tool, summary, question).
  - The elicitation's `_meta` keys are no longer fee-named (`maf-lab/write-summary`, `maf-lab/write-state`,
    `maf-lab/write-expires-at`). The billing server sends them.
- **Web.**
  - `web/src/api/types.ts` replaces `FeeAdjustmentSummary`, `ConfirmationRequiredData` and `PendingProposal` with
    `PendingWrite { writeId, toolName, summary, summarySchema, question, expiresAt }`.
  - The confirmation card renders the summary from its schema: each property's `title`, in schema order.
  - A web plugin may register its own renderer for a tool (`confirmations` in `MafWebPlugin`, keyed by tool name);
    billing registers one for `propose_fee_adjustment`, with currency formatting and the period.
- **Eval.** `EvalAgentHost` and `ConfirmationSuite` go through the seam. The suite asks the tool's flow which facts the
  question must state, instead of reading fee fields.
- **HTTP.** `GET /api/conversations/{id}/pending` answers
  `{ pending: { writeId, toolName, summary, summarySchema, question, expiresAt } | null }`. The AG-UI interrupt carries
  the same in its metadata. **BREAKING** for any client of the old `{ adjustmentId, adjustment }` shape: the only one
  is our web, changed in the same change.

## Capabilities

### New Capabilities

- `write-confirmation`: a write proposed by any tool waits in one store for the person it was put to, who confirms or
  rejects it before it expires, wherever and whenever they come back.

### Modified Capabilities

- `write-confirmation-ui`: the card shows a summary rendered from the flow's schema or the plugin's renderer, not
  fixed fee fields.
- `fee-adjustment`: billing's fee adjustment is one implementation of the write-confirmation seam. What a
  conversation is waiting on is answered by `write-confirmation`.
- `plugins`: a plugin may contribute a write-confirmation flow keyed by its write tool, and reaches the audit, the
  prompt screening, the trace and the reviewer only through the core's ports.

## Principles

- SOLID:
  - open/closed: the core's confirm, reject, expire and replay stay one algorithm, and a new write adds a flow, not a
    branch;
  - dependency inversion: a flow depends on ports owned by `Maf.Lab.Plugins.Abstractions` (`IWriteAudit`,
    `IPromptScreening`, `IWriteTraceStep`, the reviewer port), never on `ToolAudit`, `Guardrail` or `TurnTrace`;
  - single responsibility: the core keeps the waiting proposal, the flow decides about its own write.
- Standards:
  - the Strategy pattern (GoF): one `IWriteConfirmationFlow` per write tool, chosen by the tool's name. Rejected: a
    `switch` on the tool name in the core, and one generic flow with fee options;
  - ports and adapters (hexagonal), on the existing `IContributes*` seams (Orchard Core's module shape). No new kind of
    seam;
  - MCP elicitation (`elicitation/create`, `requestedSchema`) for the request for input, with its domain data in
    `_meta`, the protocol's extension point;
  - the official AG-UI interrupt for the pause in the browser (agui-protocol-only);
  - JSON Schema 2020-12 (`title`, `type`, `format`) for the summary, the format MCP elicitation already uses;
  - schema evolution that keeps data: rows renamed and reshaped in place, idempotently and race-safely across
    replicas. The mechanism, EF Core migrations or `DatabaseInitializer`'s rename step, is open question 1.
- Own: the `maf-lab/write-summary`, `maf-lab/write-state` and `maf-lab/write-expires-at` `_meta` keys. MCP leaves a
  tool's domain data to `_meta` under a vendor prefix, and there is no standard key for a write's summary. They
  replace today's fee-named keys; recorded in DECISIONS §84 (the next free section when this lands).

## Progress

None — the change adds no command and no new long step. A review that takes tens of seconds keeps saying so on the card
(write-confirmation-ui "A slow step says it is slow"), now for any flow that consults a reviewer.

## Stopping

- Key: Esc on the chat page while an answer's resume run streams; Reject on the card; the proposal's expiry
- Stop: the resume run stops through CopilotKit's stop (an aborted request), as any run does; a waiting proposal is
  stopped by Reject, or by its expiry, both recorded as a guarded update of its `PendingWrites` row, the store that owns
  it, so any replica takes the answer
- Recorded in: the proposal's `PendingWrites` row (`declined` or `expired`); the audit chain records the rejection
- Shown: the card stops offering answers once the row says `declined` or `expired`, never before

## Open questions (for the architect)

1. **Migration mechanism.** The project has no EF Core migrations: DECISIONS §17 says `DatabaseInitializer` "replaces EF
   migrations for the lab", and rename-firm-to-tenant renamed columns in place there (`RenameLegacyColumns`, one
   `BEGIN IMMEDIATE` per table). Recommended: extend that step with a `RenamedTables` list and a reshape for the
   `PendingAdjustments` → `PendingWrites` move, idempotent and race-safe as the column rename is. Alternative: adopt EF
   Core migrations now, which needs a baseline migration for every existing database, the
   `__EFMigrationsHistory` table, and the `dotnet-ef` tool, and changes how every later schema change is made.
2. **Summary rendering.** Recommended: both a schema (the default, so a plugin with no web part still gets a readable
   card) and an optional web renderer per tool. Alternative: the schema only.
3. **`_meta` keys.** Recommended: rename them to `maf-lab/write-*` in the same change, since billing's server and the
   core ship together. Alternative: keep the fee-named keys as aliases for one release.

## Documentation impact

- `docs/http-api.md`: the `/pending` row (new shape).
- `docs/plugins.md`: contributing a write-confirmation flow and a confirmation renderer.
- DECISIONS.md: a new section (the seam, the ports, the store rename, and the answer to open question 1).
- `openspec/specs/write-confirmation/spec.md` (new), and the deltas below.
