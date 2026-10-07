using System.Text.Json;
using System.Text.Json.Nodes;
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

    /// <summary>
    /// The arguments the confirmed call sends because the tool declares them. The server executes the signed state and
    /// treats these as declarative only; the flow owns its tool's argument shape, so the core reads no summary field.
    /// </summary>
    IReadOnlyDictionary<string, object?> ConfirmArguments(JsonElement summary);

    /// <summary>
    /// The core resolved the proposal: the flow records its own step. <paramref name="outcome"/> is the tool's status when
    /// it was called (e.g. <c>applied</c>, <c>already_applied</c>, <c>error</c>), or the answer's (<c>rejected</c>,
    /// <c>expired</c>).
    /// </summary>
    Task ResolvedAsync(WriteProposal proposal, WriteResolution resolution, string outcome, CancellationToken ct);
}

/// <summary>What a flow decided about a proposal.</summary>
public abstract record WriteFlowOutcome
{
    /// <summary>
    /// Put it to the person: the proposal waits for confirmation and the run pauses with this question, or with the
    /// question the tool itself asked when <paramref name="Question"/> is null.
    /// </summary>
    public sealed record AskPerson(string? Question, string? FlowJson) : WriteFlowOutcome;

    /// <summary>
    /// The person must answer something first: the model is told to ask it, and the proposal waits for input. Their
    /// answer comes back as the tool's next request in the same conversation, which is handed this proposal as open.
    /// </summary>
    public sealed record AskInput(string MessageToModel, string? FlowJson) : WriteFlowOutcome;

    /// <summary>
    /// Nothing will be confirmed: the model is told why and answers in its own words. <paramref name="Refused"/> when it
    /// was refused (e.g. by a reviewer) rather than failed.
    /// </summary>
    public sealed record TellModel(string Message, bool Refused = false) : WriteFlowOutcome;
}

/// <summary>
/// A proposal as a flow sees it. Identifiers, the summary the tool sent and the flow's own data — never the opaque state,
/// which only the core hands back to the tool. The principal is the caller's, so the tenant comes from it.
/// </summary>
/// <param name="OpenInputs">The conversation's proposals of the same tool still waiting for input.</param>
/// <param name="PersonMessage">What the person said this turn — what a reviewer is told, or a reviewer's question is answered
/// with. Content: a flow may hand it to a reviewer, never to a record or a log. Empty outside a turn.</param>
public sealed record WriteProposal(
    string WriteId,
    string ToolName,
    JsonElement Summary,
    string? FlowJson,
    Principal Principal,
    string ConversationId,
    string TurnId,
    IReadOnlyList<OpenWriteInput> OpenInputs,
    string PersonMessage = "");

/// <summary>A proposal of the same tool still waiting for the person's input.</summary>
public sealed record OpenWriteInput(string WriteId, JsonElement Summary, string? FlowJson);

/// <summary>How the core resolved a proposal.</summary>
public enum WriteResolution
{
    Applied,
    Declined,
    Expired,
    Failed,
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
    /// <param name="kind">The kind of action the record covers, e.g. <c>fee.adjustment</c>.</param>
    /// <param name="step">The step, e.g. <c>fee.adjustment.reviewed</c>.</param>
    /// <param name="identifiers">What the step was about, as <c>key=value</c> identifiers — never free text.</param>
    /// <param name="tookMs">How long the step took, when that is part of the record (a consultation's).</param>
    Task RecordAsync(string kind, string step, string identifiers, string outcome, long tookMs, CancellationToken ct);

    /// <summary>A step whose duration is not part of its record.</summary>
    Task RecordAsync(string kind, string step, string identifiers, string outcome, CancellationToken ct) =>
        RecordAsync(kind, step, identifiers, outcome, 0, ct);
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
    void Record(string kind, string title, JsonObject data);
}
