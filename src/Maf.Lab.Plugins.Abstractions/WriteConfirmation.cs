using System.Text.Json;
using Maf.Lab.Domain.Tenancy;

namespace Maf.Lab.Plugins.Abstractions;

/// <summary>
/// A plugin's write-confirmation flow for one of its write tools (generalize-write-confirmation): the Strategy the core
/// chooses by the tool's name. A factory method, so the flow takes the core's ports from the composed services.
/// </summary>
public interface IContributesWriteConfirmation
{
    IWriteConfirmationFlow CreateFlow(IServiceProvider services);
}

/// <summary>
/// What happens between a write tool's request for input (MCP's multi-round-trip <c>input_required</c>) and the person:
/// whether they are asked to confirm now, asked for something first, or not asked at all. The core keeps the proposal,
/// builds the pause, takes the answer and calls the tool; the flow decides about its own write and records its own
/// steps. It writes nothing itself — only the tool does that.
/// </summary>
public interface IWriteConfirmationFlow
{
    /// <summary>The write tool this flow is for, e.g. <c>propose_fee_adjustment</c>: the key the core chooses it by.</summary>
    string ToolName { get; }

    /// <summary>
    /// The JSON Schema (2020-12 annotations: <c>title</c>, <c>description</c>, <c>type</c>, <c>format</c>) that describes
    /// this tool's summary for display. The flow owns it; the core reads none of the summary's fields.
    /// </summary>
    JsonElement SummarySchema { get; }

    /// <summary>The tool asked for input: decide what the person is asked, if anything.</summary>
    Task<WriteFlowOutcome> ProposedAsync(WriteProposal proposal, CancellationToken ct);

    /// <summary>The core resolved the proposal (applied, declined, expired): the flow records its own step.</summary>
    Task ResolvedAsync(WriteProposal proposal, WriteResolution resolution, CancellationToken ct);
}

/// <summary>What a flow decided about a proposal.</summary>
public abstract record WriteFlowOutcome
{
    /// <summary>Put it to the person: the proposal waits for confirmation and the run pauses with this question.</summary>
    public sealed record AskPerson(string Question, string? FlowJson) : WriteFlowOutcome;

    /// <summary>
    /// The person must answer something first: the model is told to ask it, and the proposal waits for input. Their
    /// answer comes back as the tool's next request in the same conversation, which is handed this proposal as open.
    /// </summary>
    public sealed record AskInput(string MessageToModel, string? FlowJson) : WriteFlowOutcome;

    /// <summary>Nothing will be confirmed: the model is told why and answers in its own words.</summary>
    public sealed record TellModel(string Message) : WriteFlowOutcome;
}

/// <summary>
/// A proposal as a flow sees it. Identifiers, the summary the tool sent and the flow's own data — never the opaque state,
/// which only the core hands back to the tool. The principal is the caller's, so the tenant comes from it.
/// </summary>
/// <param name="OpenInputs">The conversation's proposals of the same tool still waiting for input.</param>
public sealed record WriteProposal(
    string WriteId,
    string ToolName,
    JsonElement Summary,
    string? FlowJson,
    Principal Principal,
    string ConversationId,
    string TurnId,
    IReadOnlyList<OpenWriteInput> OpenInputs);

/// <summary>A proposal of the same tool still waiting for the person's input.</summary>
public sealed record OpenWriteInput(string WriteId, JsonElement Summary, string? FlowJson);

/// <summary>How the core resolved a proposal.</summary>
public enum WriteResolution
{
    Applied,
    Declined,
    Expired,
}

/// <summary>
/// The facts the question put to a person must state, for the confirmation eval (interface segregation: not part of the
/// production flow). Optional; resolved by tool name.
/// </summary>
public interface IStatesConfirmationFacts
{
    string ToolName { get; }

    /// <summary>The values, as the person should read them, that the question must contain.</summary>
    IReadOnlyList<string> FactsToState(JsonElement summary);
}

/// <summary>
/// The audit record of a write's steps (a Port the core implements over its audit chain). Identifiers only: the acting
/// person and tenant come from the request's principal, never from a parameter.
/// </summary>
public interface IWriteAudit
{
    /// <param name="Step">A dotted name the flow chooses, e.g. <c>fee.adjustment.reviewed</c>.</param>
    Task RecordAsync(string step, string toolName, string writeId, string outcome, CancellationToken ct);
}

/// <summary>
/// The guard on a reviewer's words before a flow believes or relays them (a Port the core implements over its
/// guardrail). The person's own text is screened at turn entry, not here.
/// </summary>
public interface IConsultationScreening
{
    Task<ConsultationResult> ScreenAsync(ConsultationResult result, CancellationToken ct);
}

/// <summary>A step of the current turn's trace (a Port the core implements over the turn's trace). No message content.</summary>
public interface IWriteTraceStep
{
    void Record(string kind, JsonElement data);
}

/// <summary>
/// The wire keys of a confirmed write, owned by the core so no plugin and no core file names another plugin's: the
/// summary, state and expiry in the input request's <c>_meta</c>, the idempotency key in the confirmed call's
/// <c>_meta</c>, and the key the tool's request for input declares for the confirmation.
/// </summary>
public static class WriteConfirmationKeys
{
    public const string Summary = "maf-lab/write-summary";
    public const string State = "maf-lab/write-state";
    public const string ExpiresAt = "maf-lab/write-expires-at";
    public const string IdempotencyKey = "maf-lab/idempotencyKey";
    public const string Confirmation = "confirmation";
}
