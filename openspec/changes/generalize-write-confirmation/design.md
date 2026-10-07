# Design

## Context

This is what the core holds about the fee write today, from reading the code at `7a54678` / `57780fc` (2026-10-07).
Reference counts are from the architect's sweep.

- `Agent/ConfirmationService.cs` (16 references): `AnswerAsync` / `ResumeAsync` and `ConfirmationOutcome.Applied(FeeAdjustmentOutcomeDto)`.
- `Agent/ConfirmationSink.cs` (5): `CapturedConfirmation(FeeAdjustmentSummary Adjustment, string State, string Question,
  DateTimeOffset? ExpiresAt, JsonElement? AnswerSchema)`. It reads `FeeAdjustmentTool.SummaryKey` (`maf-lab/adjustment`),
  `StateKey` and `ExpiresAtKey` from the elicitation's `_meta`.
- `Storage/MafDbContext.cs` (6): `PendingAdjustmentRow` (`Id, TenantId, UserId, ConversationId, TurnId, ToolName, State,
  Summary, Question, ExpiresAt, Status, ReviewTaskId, Questions, CreatedAt, UpdatedAt`) and `PendingAdjustmentStatus`
  (`awaiting_confirmation`, `awaiting_justification`, `applied`, `declined`, `refused`, `failed`).
- `Endpoints/HistoryEndpoints.cs` (3): `GET /api/conversations/{id}/pending`.
- `Agent/ChatTurnRunner.cs` (3): `TurnResult.Proposal` (`FeeAdjustmentSummary`, :30) and `ProposedAsync` (:1033, :1377).
- Smaller references:
  - `RunRejoin` (2): replays an `awaiting_confirmation` row;
  - `ToolSource` (2): `IdempotencyMetaKey` and `ConfirmationKey` on the confirmed call;
  - `Program` (2), `MessageRetentionService` (1), `AuditChain` (1).
- `Agent/FeeAdjustmentFlow.cs`: takes `ComplianceConsultant`, `Guardrail`, `ToolAudit`, `IDbContextFactory<MafDbContext>`,
  `TurnTrace` and `PersonQuestion`. It returns `FlowOutcome.AskUser(PersonQuestion)` or `TellModel(string)`, and records
  `fee.adjustment.{proposed,reviewed,confirmed,rejected,applied}`.
- Web: `web/src/api/types.ts` (`FeeAdjustmentSummary`, `ConfirmationRequiredData`, `PendingProposal`) and
  `chat/ConfirmationCard.tsx`. `MafWebPlugin.cards` already maps an `activityType` to a component, so the confirmation
  renderer follows that pattern.
- Eval: `EvalAgentHost` and `ConfirmationSuite`, which checks that the question states the account, the amount and the
  resulting fee.

## Goals / Non-Goals

- Goals:
  - the core confirms, rejects, expires and replays any tool's write, knowing only the tool's name;
  - a plugin contributes the decision before the person is asked, and how the summary is shown;
  - the existing waiting proposals survive the change.
- Non-goals:
  - moving billing's files into `plugins/billing/` (extract-billing does that);
  - changing the fee rules;
  - a second write tool (none exists yet; `_example` may show one later).

## Decisions

### The seam (Strategy)

```csharp
// Maf.Lab.Plugins.Abstractions
public interface IContributesWriteConfirmation
{
    IWriteConfirmationFlow CreateFlow(IServiceProvider services);
}

public interface IWriteConfirmationFlow
{
    string ToolName { get; }                    // the key: e.g. "propose_fee_adjustment"
    JsonElement SummarySchema { get; }          // JSON Schema 2020-12 of the summary it puts to a person

    // A write tool asked for input: decide whether a person is asked now, or the model is told why not.
    Task<WriteFlowOutcome> ProposedAsync(WriteProposal proposal, CancellationToken ct);

    // The person answered a question the flow asked (e.g. a reviewer's request for a justification).
    Task<WriteFlowOutcome> InputAsync(WriteProposal proposal, string text, CancellationToken ct);

    // The core resolved the proposal (confirmed and applied, rejected, expired): the flow records its own step.
    Task ResolvedAsync(WriteProposal proposal, WriteResolution resolution, CancellationToken ct);

    // The facts the person's question must state, for the eval (ConfirmationSuite).
    IReadOnlyList<string> FactsToState(JsonElement summary);
}

public abstract record WriteFlowOutcome
{
    public sealed record AskPerson(string Question, JsonElement Summary, string? FlowJson) : WriteFlowOutcome;
    public sealed record AskInput(string Question, string? FlowJson) : WriteFlowOutcome;   // awaiting_input
    public sealed record TellModel(string Message) : WriteFlowOutcome;
}
```

- `WriteProposal` is `(writeId, toolName, state, summary, flowJson, principal ids, conversationId, turnId)`. The core
  builds it from the elicitation and from the `PendingWrites` row.
- The ports below are owned by Abstractions and implemented by the core. The flow takes them from `IServiceProvider` in
  `CreateFlow`:
  - `IWriteAudit.RecordAsync(step, toolName, writeId, outcome)`: the step is a dotted name the flow chooses (billing
    keeps `fee.adjustment.*`). Identifiers only.
  - `IPromptScreening.ScreenAsync(text)`: the core's `Guardrail` check of text a person types (a justification).
  - `IWriteTraceStep.Record(kind, data)`: one step in the turn's trace, through the core's `TurnTrace`.
  - The reviewer consultation: extract-billing part 3's port, used as it is.
- The core keeps the algorithm: capture the elicitation, find the flow by `ToolName` (no flow means the write is
  refused and the model is told so), persist the row, raise the AG-UI interrupt, and on an answer confirm with the
  signed state, or reject, or expire. Replay on rejoin is part of it too.

### The store

- `PendingWrites` has the columns `Id, TenantId, UserId, ConversationId, TurnId, ToolName, State, Summary (JSON),
  Question, ExpiresAt, Status, FlowJson (JSON, nullable), CreatedAt, UpdatedAt`.
- The statuses are `awaiting_confirmation`, `awaiting_input`, `applied`, `declined`, `refused`, `failed` and `expired`.
- Every status change is one guarded update (`WHERE Status = <expected>`), as today. That is what lets any replica take
  the answer.
- Existing rows:
  - the table is renamed;
  - `awaiting_justification` becomes `awaiting_input`;
  - `ReviewTaskId` and `Questions` move into `FlowJson` (`{"reviewTaskId": …, "questions": n}`);
  - `Summary` is already the fee summary's JSON and stays as it is.
  - The mechanism is open question 1 of the proposal. The recommended one is `DatabaseInitializer`'s rename step: one
    `BEGIN IMMEDIATE` per table, a no-op once done, and tolerating a replica that lost the race.

### Wire

- MCP: the elicitation's `_meta` carries `maf-lab/write-summary` (the summary), `maf-lab/write-state` (the opaque
  state) and `maf-lab/write-expires-at`. `requestedSchema` stays the answer's schema.
- AG-UI: the interrupt's metadata carries `{ writeId, toolName, summary, summarySchema, question, expiresAt }`, built only
  in `Agent/AGUI/AGUIMappings.cs`.
- HTTP: `GET /api/conversations/{id}/pending` answers `{ pending: PendingWrite | null }` in the same shape.

### Web

- `ConfirmationCard` renders `summary` by `summarySchema`: each property's `title`, in schema order, formatted by
  `type`/`format`.
- `MafWebPlugin.confirmations?: Record<toolName, ComponentType<{ summary: unknown }>>` overrides that rendering for one
  tool. Billing registers `propose_fee_adjustment` (currency, period).

## Risks / Trade-offs

- **Breaking HTTP/interrupt shape.** Our web is the only client, and it changes in the same change.
- **A plugin without a flow for its write tool.** Its write is refused (the model is told). The tool is never called
  confirmed.
- **Overlap with extract-billing part 3** (`Program.cs`, `FeeAdjustmentFlow.cs`, the reviewer port). The code phase
  starts only after the architect fences the paths against Pepi's working tree.

## Migration Plan

1. Abstractions: the seam and the ports. The core: the ports' adapters and a flow registry built from the installed
   plugins.
2. The store rename and reshape (the mechanism from open question 1), with a test on a database holding old rows.
3. The core goes generic (`ConfirmationService`, `ConfirmationSink`, `ChatTurnRunner`, `RunRejoin`, `HistoryEndpoints`,
   `ToolSource`, `MessageRetentionService`, `AuditChain`).
4. `FeeAdjustmentFlow` moves onto the seam; billing contributes it and its web renderer.
5. Web types and the card; the eval host and `ConfirmationSuite`; docs.
