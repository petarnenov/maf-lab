namespace Maf.Lab.Domain.SharedState;

/// <summary>How a run ended, or that it has not.</summary>
public static class RunOutcomes
{
    public const string Running = "running";
    public const string Answered = "answered";
    public const string AwaitingPerson = "awaiting_person";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}

/// <summary>One tool call of a run, as a client that was away needs to see it: what, and how it went.</summary>
public sealed record RunToolCall(string CallId, string ToolName, string ArgumentSummary, string? ResultSummary, bool Finished, bool IsError);

/// <summary>
/// Where a run stands, readable by every replica. It is a snapshot and not a recording: a client that comes back
/// is told what the turn has said and done, not the frames it missed.
/// </summary>
/// <param name="Outcome">One of <see cref="RunOutcomes"/>.</param>
/// <param name="AwaitingId">The interrupt a paused run is waiting on, so the client can ask about it.</param>
public sealed record RunState(
    string RunId,
    string ConversationId,
    string UserId,
    string FirmId,
    string Answer,
    IReadOnlyList<RunToolCall> ToolCalls,
    string Outcome,
    string? AwaitingId,
    string? TurnId,
    string? Error,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt);
