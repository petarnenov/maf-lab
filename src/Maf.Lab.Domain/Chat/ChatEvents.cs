using System.Text.Json.Serialization;

namespace Maf.Lab.Domain.Chat;

/// <summary>SSE event names, in the order a tool-using turn emits them.</summary>
public static class ChatEventNames
{
    public const string TextDelta = "text_delta";
    public const string ToolCallStarted = "tool_call_started";
    public const string ToolCallFinished = "tool_call_finished";
    public const string Sources = "sources";
    /// <summary>A turn that ends waiting for a person to approve or reject a write.</summary>
    public const string ConfirmationRequired = "confirmation_required";
    public const string Done = "done";
    public const string Trace = "trace";
}

public abstract record ChatEvent
{
    [JsonIgnore]
    public abstract string EventName { get; }
}

public sealed record TextDeltaEvent(string Text) : ChatEvent
{
    [JsonIgnore]
    public override string EventName => ChatEventNames.TextDelta;
}

public sealed record ToolCallStartedEvent(string CallId, string ToolName, string ArgumentSummary) : ChatEvent
{
    [JsonIgnore]
    public override string EventName => ChatEventNames.ToolCallStarted;
}

public sealed record ToolCallFinishedEvent(string CallId, string ToolName, string ResultSummary, int SourceCount, bool IsError) : ChatEvent
{
    [JsonIgnore]
    public override string EventName => ChatEventNames.ToolCallFinished;
}

public sealed record SourcesEvent(IReadOnlyList<SourceRef> Sources) : ChatEvent
{
    [JsonIgnore]
    public override string EventName => ChatEventNames.Sources;
}

public sealed record SourceRef(string DocId, string SectionPath, string SourcePath, string Snippet);

/// <summary>
/// A write is waiting for a person. Carries what they need to check and the opaque state that says what
/// would be executed; the state is not for reading, only for handing back with an answer.
/// </summary>
public sealed record ConfirmationRequiredEvent(
    string CallId,
    string ToolName,
    string AdjustmentId,
    Billing.FeeAdjustmentSummary Adjustment,
    string Question,
    string State) : ChatEvent
{
    [JsonIgnore]
    public override string EventName => ChatEventNames.ConfirmationRequired;
}

/// <summary>What a person answers with.</summary>
public sealed record ConfirmationDecision(string ConversationId, string AdjustmentId, bool Approve);

/// <summary>Last event of a turn. <paramref name="Error"/> is short user-facing text when the turn failed.</summary>
public sealed record DoneEvent(string ConversationId, string TurnId, string? Error = null) : ChatEvent
{
    [JsonIgnore]
    public override string EventName => ChatEventNames.Done;
}

public sealed record ChatRequest(string? ConversationId, string Message);

public sealed record ConversationCreated(string ConversationId);
