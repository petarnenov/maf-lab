# Proposal

## Why

Asking a person before a write happens is a core capability, but today the core only knows one write: the fee
adjustment. These core files name `FeeAdjustmentSummary`, `FeeAdjustmentTool` or `PendingAdjustmentStatus`:

- `ConfirmationService`, `ConfirmationSink`, `MafDbContext` (`PendingAdjustments`);
- `HistoryEndpoints` (`/pending`), `ChatTurnRunner` (`TurnResult.Proposal`, `ProposedAsync`), `RunRejoin`;
- `ToolSource`, `Program`, `MessageRetentionService` and `AuditChain`.

`FeeAdjustmentFlow` lives in the api. It holds the reviewer, the guard on the reviewer's words, the audit, the trace and
the person's question. Extract-billing part 3 fenced six of these files in `CoreNamesNoDomainTests`, marked "until the
generalize-write-confirmation follow-up". The web's `ConfirmationCard` renders fixed fee fields.

This blocks the plugin rollout in two ways. Billing cannot become a plugin without leaving its write flow in the core.
And no other plugin can ask a person before it writes without adding another branch to the core.

This change comes right after extract-billing in the confirmed order. It builds on extract-billing part 3's
`IReviewerConsultation` port (`Maf.Lab.Plugins.Abstractions/ReviewerConsultation.cs`) and does not redefine it.

## What Changes

- **New capability `write-confirmation` (server).**
  - A write tool's first call returns the protocol's request for input (MCP's multi-round-trip `input_required`,
    DECISIONS §25) with a summary and an opaque state. It never returns a change.
  - The core keeps every proposal in one pending-writes store. It confirms, rejects and expires proposals, and it
    replays a waiting proposal when a run is rejoined. It does this for any write tool, by name.
- **A Strategy per write tool.** `Maf.Lab.Plugins.Abstractions` gains `IWriteConfirmationFlow`, keyed by the tool's
  name and contributed by a plugin (`IContributesWriteConfirmation`).
  - The flow owns the JSON Schema of its summary (`SummarySchema`).
  - It decides between three outcomes: ask the person to confirm; tell the model to ask the person for input, with the
    row waiting for that input (`awaiting_input`); or tell the model why not.
  - It is handed its own conversation's open `awaiting_input` rows for the same tool. A justification arrives this
    way: the advisor's typed answer comes back through the model as a second call of the same tool, and the flow
    continues the same review.
  - When a proposal resolves, the core resolves the same tool's other open rows in that conversation. That is a core
    rule, not the flow's.
  - The flow reaches the core only through ports: `IReviewerConsultation` (part 3), plus three new ones:
    - `IWriteAudit`, the audit record;
    - `IConsultationScreening`, the guard on the reviewer's words (`Guardrail.ScreenConsultationAsync`);
    - `IWriteTraceStep`, a step in the turn's trace.
  - What the eval checks a question for is an optional, separate interface, `IStatesConfirmationFacts`, resolved by
    tool name. It is not part of the flow (interface segregation).
- **Billing implements the seam.** `FeeAdjustmentFlow` becomes billing's flow for `propose_fee_adjustment`, written
  against the ports. The fee rules are unchanged: the review threshold, at most two questions, the verdict check,
  applied at most once, never below zero.
- **One store, rows kept.** `DatabaseInitializer` renames `PendingAdjustments` to `PendingWrites` in place, with a new
  `RenamedTables` step that runs before the column renames:
  - one `BEGIN IMMEDIATE` per table;
  - it checks `sqlite_master` for the old name without the new one;
  - it drops the `IX_PendingAdjustments_*` indexes, which the index pass recreates under the new name;
  - it runs `ALTER TABLE … RENAME TO` and tolerates a losing replica's "no such table".
  - After that:
    - the additive pass adds `FlowJson`;
    - `BackfillAsync` moves `awaiting_justification` to `awaiting_input` and copies `ReviewTaskId`/`Questions` into
      `FlowJson`, both idempotently;
    - the old columns stay unmapped, with no `DROP COLUMN`.
  - A new status, `expired`, is written when an answer or a rejoin finds the proposal past its expiry.
- **One builder for the pause.** A single core builder turns a `PendingWrite` into the `PersonQuestion` that
  `TurnContents.Ask` hands to the official AG-UI server. Today two places compose that metadata
  (`FeeAdjustmentFlow.Ask` and `RunRejoin.QuestionAsync`); after this change the turn and the rejoin both use the builder.
  Nothing is added to `AGUIMappings.cs`. The opaque state is no longer in the interrupt's metadata: the answer resolves
  the row's state by the proposal's id, so the state never leaves the server, as `docs/http-api.md` already says.
- **A summary the core does not read.** `CapturedConfirmation` carries the summary as a `JsonElement`.
  `TurnResult.Proposal` becomes a `PendingWrite` (tool, summary, question). `/pending` and the rejoin look the schema
  up from the flow by tool name, and a tool with no flow degrades to no schema.
- **Five wire keys owned by the core.** `WriteConfirmationKeys` in Abstractions replaces the fee-named constants, with
  no aliases:
  - `maf-lab/write-summary`, `maf-lab/write-state` and `maf-lab/write-expires-at`, in the input request's `_meta`;
  - `maf-lab/idempotencyKey`, in the confirmed call's `_meta`;
  - `confirmation`, the request-for-input dictionary key the server declares.
- **Web.**
  - `web/src/api/types.ts` replaces `FeeAdjustmentSummary`, `ConfirmationRequiredData` and `PendingProposal` with
    `PendingWrite { writeId, toolName, summary, summarySchema, question, expiresAt }`.
  - The card is display only: a key/value `<dl>` built from each property's `title` in property order, formatted by
    `type`/`format` for numbers and dates. That is JSON Schema 2020-12's own annotation vocabulary, with no form library.
  - `MafWebPlugin.confirmations: Record<toolName, ComponentType<{ summary: unknown; schema: unknown }>>`, mirroring
    `cards`, overrides the card for one tool. Billing registers `propose_fee_adjustment`.
  - The consumers that change with it: `useChatStream.ts`, `chatReducer.ts` (`interruptToConfirmation`),
    `ConfirmationCard.test.tsx` and `curriculum.ts`.
- **Eval.** `EvalAgentHost` and `ConfirmationSuite` go through the seam, with the facts from `IStatesConfirmationFacts`.
- **HTTP.** `GET /api/conversations/{id}/pending` answers
  `{ pending: { writeId, toolName, summary, summarySchema, question, expiresAt } | null }`, and stays read-only. The
  interrupt's metadata has the same shape. **BREAKING** for the old `{ adjustmentId, adjustment }`: our web is the only
  client, and it changes in the same change.
- **Allow-list.** The six `CoreNamesNoDomainTests` entries marked "until the generalize-write-confirmation follow-up"
  are removed, so the existing test now holds for these files.

## Capabilities

### New Capabilities

- `write-confirmation`: a write proposed by any tool waits in one store for the person it was put to, who confirms or
  rejects it before it expires, wherever and whenever they come back.

### Modified Capabilities

- `write-confirmation-ui`: the card shows a summary rendered by the plugin's renderer or from the flow's schema, not
  fixed fee fields.
- `fee-adjustment`: billing's fee adjustment is one implementation of the write-confirmation seam. What a
  conversation is waiting on is answered by `write-confirmation`.
- `plugins`: a plugin may contribute a write-confirmation flow keyed by its write tool. It reaches the audit, the
  screening of a reviewer's words, the trace and the reviewer only through the core's ports.

## Principles

- SOLID:
  - open/closed: confirm, reject, expire and replay stay one algorithm in the core, and a new write adds a flow, not a
    branch;
  - dependency inversion: a flow depends on ports owned by `Maf.Lab.Plugins.Abstractions` (`IReviewerConsultation`,
    `IWriteAudit`, `IConsultationScreening`, `IWriteTraceStep`), never on `ComplianceConsultant`, `ToolAudit`,
    `Guardrail` or `TurnTrace`;
  - interface segregation: what the eval needs (`IStatesConfirmationFacts`) is not part of the production flow;
  - single responsibility: the core keeps the waiting proposal and builds the pause, and the flow decides about its own
    write.
- Standards:
  - the Strategy pattern (GoF): one `IWriteConfirmationFlow` per write tool, chosen by the tool's name. Rejected: a
    `switch` on the tool name in the core, and one generic flow with fee options;
  - ports and adapters (hexagonal), on the existing `IContributes*` seams (Orchard Core's module shape), with no new
    kind of seam;
  - MCP's multi-round-trip request (`input_required`, which the client surfaces to its elicitation handler) for the
    request for input, as DECISIONS §25 chose over `elicitation/create`, with its domain data in `_meta`, the
    protocol's extension point;
  - the official AG-UI interrupt (`InterruptRequestContent` through `TurnContents.Ask`, mapped by the official server)
    for the pause (agui-protocol-only);
  - JSON Schema 2020-12's annotation vocabulary (§9.1: `title`, `description`; `type`, `format`) to describe the
    summary for display, the format MCP's input requests use for their schemas. Rejected: a JSON-Schema form library such as rjsf, a
    dependency for a form nobody fills;
  - schema evolution that keeps data, in the project's own established mechanism: `DatabaseInitializer`, idempotent
    and race-safe across replicas, as rename-firm-to-tenant's column rename was. Rejected: adopting EF Core migrations
    now (the `dotnet-ef` tool, a baseline migration for every existing database, `__EFMigrationsHistory`, and a
    package move that changes every later schema change).
- Own: the five `WriteConfirmationKeys` (`maf-lab/write-summary`, `maf-lab/write-state`, `maf-lab/write-expires-at`,
  `maf-lab/idempotencyKey`, `confirmation`). MCP leaves a tool's domain data to `_meta` under a vendor prefix, and
  there is no standard key for a write's summary, state or idempotency. They replace the fee-named keys DECISIONS §25 introduced; the next free DECISIONS section at
  landing amends §25.

## Progress

None — the change adds no command and no new long step. A review that takes tens of seconds keeps saying so on the card
(write-confirmation-ui "A slow step says it is slow"), now for any flow that consults a reviewer.

## Stopping

- Key: Esc on the chat page while a turn or an answer's resume run streams; Reject on the card; the proposal's expiry
- Stop: the run stops through CopilotKit's stop (an aborted request), as any run does. A reviewer consultation in
  progress inside `ProposedAsync` is cancelled with the turn by A2A `tasks/cancel`, through `IReviewerConsultation`'s
  implementation, unchanged. A waiting proposal is stopped by Reject or by its expiry, each one guarded update of its
  `PendingWrites` row, the store that owns it, so any replica takes the answer
- Recorded in: the proposal's `PendingWrites` row (`declined` or `expired`); the audit chain records the rejection
- Shown: the card stops offering answers once the row says `declined` or `expired`, never before

## Stages (the architect's fence)

- **Stage A**, now, beside extract-billing. It touches only:
  - new Abstractions files (the seam, the ports, `WriteConfirmationKeys`);
  - `DatabaseInitializer`'s `RenamedTables` step as a mechanism, with an empty list, and its test. The
    `PendingAdjustments` entry lands in Stage B with the `MafDbContext` mapping: renaming the table while the model
    still maps the old name would make the create pass add an empty table under the old name;
  - the web: `PendingWrite`, a display-only `WriteSummary` component and the `confirmations` registry, all added
    beside today's fee types, which go in Stage B.
- **Stage B**, after extract-billing is archived: everything else. That is the `MafDbContext` mapping, the core's
  confirmation files, the billing server's keys (in `plugins/billing/` by then), the eval, the allow-list, the docs and
  DECISIONS.

## Documentation impact

- `docs/http-api.md`: the `/pending` row gets its new shape, and the "state is deliberately absent" wording loses its
  fee terms.
- `docs/plugins.md`: contributing a write-confirmation flow and a confirmation renderer.
- DECISIONS.md, in the next free section at landing:
  - the seam, the ports and the five keys;
  - the table rename in `DatabaseInitializer`, which amends §17's "Destructive or renaming changes would need a real
    migration": a rename is done in the initializer, and EF Core migrations were rejected.
- `openspec/specs/write-confirmation/spec.md` (new), and the deltas below.
