# Design

## Context

This is what the core holds about the fee write today, from reading the code at `8c76061` (2026-10-07). Reference counts
are from the architect's sweep.

- `Agent/ConfirmationService.cs` (16 references): `AnswerAsync`, `ResumeAsync`, and
  `ConfirmationOutcome.Applied(FeeAdjustmentOutcomeDto)`.
- `Agent/ConfirmationSink.cs` (5):
  - `CapturedConfirmation(FeeAdjustmentSummary Adjustment, string State, string Question, DateTimeOffset? ExpiresAt,
    JsonElement? AnswerSchema)`;
  - it reads `FeeAdjustmentTool.SummaryKey` (`maf-lab/adjustment`), `StateKey` (`maf-lab/proposal-state`) and
    `ExpiresAtKey` (`maf-lab/proposal-expires-at`) from the input request's `_meta`.
- `Agent/ToolSource.cs` (2): `IdempotencyMetaKey` (`maf-lab/idempotencyKey`) and `ConfirmationKey` (`confirmation`, the
  request-for-input dictionary key the server declares at `FeeAdjustmentTools.cs:150`).
- `Storage/MafDbContext.cs` (6):
  - `PendingAdjustmentRow (Id, TenantId, UserId, ConversationId, TurnId, ToolName, State, Summary, Question, ExpiresAt,
    Status, ReviewTaskId, Questions, CreatedAt, UpdatedAt)`;
  - `PendingAdjustmentStatus`: `awaiting_confirmation`, `awaiting_justification`, `applied`, `declined`, `refused`,
    `failed`;
  - indexes `(TenantId, UserId, UpdatedAt)` and `(ConversationId, UpdatedAt)`.
- `Agent/ChatTurnRunner.cs` (3): `TurnResult.Proposal` (`FeeAdjustmentSummary`, :30) and `ProposedAsync` (:1033, :1377).
- `Agent/FeeAdjustmentFlow.cs`. Since part 3 it takes `IReviewerConsultation`, plus `Guardrail`, `ToolAudit`,
  `IDbContextFactory<MafDbContext>`, `TurnTrace` and `PersonQuestion`.
  - It returns `FlowOutcome.AskUser(PersonQuestion)` or `TellModel(string)`, and records
    `fee.adjustment.{proposed,reviewed,confirmed,rejected,applied}`.
  - A justification is not an input interrupt. The model is told to ask, the advisor types, and the model calls
    `propose_fee_adjustment` again. `OpenReviewAsync` then finds the `awaiting_justification` row for the same account.
  - `Resolve` also marks sibling `awaiting_justification` rows as resolved.
- The pause: `TurnContents.Ask(PersonQuestion)` makes the `InterruptRequestContent` that the official AG-UI server maps.
  Its metadata is composed in two places, `FeeAdjustmentFlow.Ask` and `RunRejoin.QuestionAsync`. `FeeAdjustmentFlow.Ask`
  also puts the opaque state in it, although `docs/http-api.md` says the state never leaves the run.
- `RunRejoin` (2) replays an `awaiting_confirmation` row and does not check expiry. `HistoryEndpoints` (3) serves
  `GET /api/conversations/{id}/pending`.
- `Guardrail.ScreenConsultationAsync(ConsultationResult, trace, callId)` screens the reviewer's words. What the person
  types is screened at turn entry, not here.
- `CoreNamesNoDomainTests` allow-lists six of these files with the marker "until the generalize-write-confirmation
  follow-up".
- Web:
  - `web/src/api/types.ts` (`FeeAdjustmentSummary`, `ConfirmationRequiredData`, `PendingProposal`);
  - `chat/ConfirmationCard.tsx` and its test;
  - `chat/chatReducer.ts` (`interruptToConfirmation` reads `metadata.tool`/`adjustment`);
  - `chat/useChatStream.ts` and `curriculum/curriculum.ts`.
  - `MafWebPlugin.cards` already maps an `activityType` to a component.
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
  - a second write tool.

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
    string ToolName { get; }                // the key, e.g. "propose_fee_adjustment"
    JsonElement SummarySchema { get; }      // JSON Schema 2020-12 annotations of the summary; the flow owns it

    // A write tool asked for input (MCP input_required). The proposal carries the conversation's open awaiting_input rows for this tool.
    Task<WriteFlowOutcome> ProposedAsync(WriteProposal proposal, CancellationToken ct);

    // The core resolved the proposal (applied, declined, expired): the flow records its own step.
    Task ResolvedAsync(WriteProposal proposal, WriteResolution resolution, CancellationToken ct);
}

public abstract record WriteFlowOutcome
{
    // Put it to the person: the row is awaiting_confirmation and the run pauses.
    public sealed record AskPerson(string Question, string? FlowJson) : WriteFlowOutcome;

    // Tell the model to ask the person for input: the row is awaiting_input with FlowJson; no pause.
    public sealed record AskInput(string MessageToModel, string? FlowJson) : WriteFlowOutcome;

    // Nothing will be confirmed; the model is told why.
    public sealed record TellModel(string Message) : WriteFlowOutcome;
}

// Optional, for the eval only (interface segregation): the facts a question must state.
public interface IStatesConfirmationFacts
{
    string ToolName { get; }
    IReadOnlyList<string> FactsToState(JsonElement summary);
}

public static class WriteConfirmationKeys
{
    public const string Summary = "maf-lab/write-summary";        // input request _meta
    public const string State = "maf-lab/write-state";            // input request _meta
    public const string ExpiresAt = "maf-lab/write-expires-at";   // input request _meta
    public const string IdempotencyKey = "maf-lab/idempotencyKey"; // confirmed call _meta
    public const string Confirmation = "confirmation";            // request-for-input dictionary key
}
```

- `WriteProposal` is `(WriteId, ToolName, Summary, FlowJson, TenantId, UserId, ConversationId, TurnId, OpenInputs)`.
  `OpenInputs` holds the conversation's `awaiting_input` rows for the same tool (`WriteId`, `Summary`, `FlowJson`), so
  billing's flow continues the review the reviewer paused, as `OpenReviewAsync` does today. The opaque state is not in
  it: only the core hands the state back to the tool.
- When a proposal resolves, the core marks the same tool's other open rows in that conversation as resolved. Today
  billing's `Resolve` does that; after this change it is a core rule.
- The ports are owned by Abstractions, implemented by the core, and taken from `IServiceProvider` in `CreateFlow`:
  - `IReviewerConsultation`: extract-billing part 3's, used as it is;
  - `IWriteAudit.RecordAsync(step, toolName, writeId, outcome)`: the step is a dotted name the flow chooses (billing keeps
    `fee.adjustment.*`). Identifiers only;
  - `IConsultationScreening.ScreenAsync(ConsultationResult)`: the core's `Guardrail.ScreenConsultationAsync` on the
    reviewer's words;
  - `IWriteTraceStep.Record(kind, data)`: one step in the turn's trace, through the core's `TurnTrace`.
- The core keeps the algorithm:
  1. capture the input request (the client's elicitation handler, `ConfirmationSink`);
  2. find the flow by `ToolName`. No flow means the write is refused and the model is told so;
  3. persist the row;
  4. on `AskPerson`, pause with the built question;
  5. on an answer, confirm with the row's state, reject, or expire;
  6. replay on rejoin.

### One builder for the pause

`PendingWriteQuestion.From(PendingWrite, IWriteConfirmationFlow?) → PersonQuestion` is the only place that composes the
interrupt's metadata: `{ writeId, toolName, summary, summarySchema, question, expiresAt }`.

- The opaque state is not in it. The answer names the proposal by id, and the core reads `row.State` by that id.
- The turn uses it, and so does `RunRejoin.QuestionAsync`, both through `TurnContents.Ask`.
- `AGUIMappings.cs` is unchanged: the official server maps the `InterruptRequestContent`.

### The store

- `PendingWrites` maps these columns: `Id, TenantId, UserId, ConversationId, TurnId, ToolName, State, Summary (JSON),
  Question, ExpiresAt, Status, FlowJson (JSON, nullable), CreatedAt, UpdatedAt`. `ReviewTaskId` and `Questions` stay in
  the table, unmapped.
- The statuses are `awaiting_confirmation`, `awaiting_input`, `applied`, `declined`, `refused`, `failed` and `expired`.
  Every change is one guarded update from the expected status, as today.
- `expired` is written only when an answer finds the proposal past its expiry (`ConfirmationService`) or when a rejoin
  does (`RunRejoin`, which gains the expiry check). `GET /pending` stays read-only and filters expired rows out, as now.
- `DatabaseInitializer`:
  - `RenamedTables = [("PendingAdjustments", "PendingWrites")]` runs before `RenameLegacyColumnsAsync`. Per table it
    does one `BEGIN IMMEDIATE`:
    1. check `sqlite_master` for the old name without the new one, otherwise commit and return;
    2. drop the `IX_PendingAdjustments_*` indexes;
    3. `ALTER TABLE "PendingAdjustments" RENAME TO "PendingWrites"`;
    4. commit. A losing replica's "no such table" is tolerated.
  - The additive pass adds `FlowJson`, and the index pass recreates the indexes as `IX_PendingWrites_*`.
  - `BackfillAsync` runs two idempotent updates:
    - `UPDATE "PendingWrites" SET "Status" = 'awaiting_input' WHERE "Status" = 'awaiting_justification'`;
    - `UPDATE "PendingWrites" SET "FlowJson" = json_object('reviewTaskId', "ReviewTaskId", 'questions', "Questions")
      WHERE "FlowJson" IS NULL AND ("ReviewTaskId" IS NOT NULL OR "Questions" > 0)`. It is guarded on the old columns
      existing, because a fresh database never had them.

### Web

- `PendingWrite { writeId, toolName, summary, summarySchema, question, expiresAt }` replaces the three fee types.
- `ConfirmationCard` is display only: a `<dl>` of each property's `title` in property order, with numbers and dates
  formatted by `type`/`format`. With no schema it shows nothing beyond the question.
- `MafWebPlugin.confirmations?: Record<toolName, ComponentType<{ summary: unknown; schema: unknown }>>` mirrors `cards`
  and overrides the card for one tool. Billing registers `propose_fee_adjustment` (currency, period).

## Risks / Trade-offs

- **Breaking `/pending` and interrupt shape.** Our web is the only client, and it changes in the same change.
- **A tool with no flow.** Its write is refused and the model is told. The tool is never called confirmed.
- **Overlap with extract-billing.** Handled by the stages below, fenced by the architect against Pepi's working tree.

## Migration Plan

- **Stage A** (now):
  1. Abstractions: the seam, the ports and `WriteConfirmationKeys` (new files only).
  2. `DatabaseInitializer`: the `RenamedTables` step as a mechanism with an empty list, plus a test that renames a
     test table holding rows, run by concurrent initializers. The `PendingAdjustments` entry is NOT added here: while
     `MafDbContext` still maps `PendingAdjustments`, renaming it would make the create pass add an empty table under the
     old name, and the app would read that one.
  3. Web: `PendingWrite`, the schema-driven card and the `confirmations` registry.
- **Stage B** (after extract-billing is archived):
  1. `MafDbContext` maps `PendingWrites`. In the same commit, `RenamedTables` gains `("PendingAdjustments",
     "PendingWrites")` and `BackfillAsync` gains the two updates, with a test on a database holding old rows.
  2. The core goes generic: `ConfirmationSink`, `ConfirmationService`, `ChatTurnRunner`, `RunRejoin`, `HistoryEndpoints`,
     `ToolSource`, `Program`, `MessageRetentionService`, `AuditChain`; plus the pause builder and the ports' adapters.
  3. Billing's flow on the seam; its server sends the `WriteConfirmationKeys`; its web renderer.
  4. The eval (`EvalAgentHost`, `ConfirmationSuite` through `IStatesConfirmationFacts`).
  5. The `CoreNamesNoDomainTests` allow-list entries removed; docs; DECISIONS.
