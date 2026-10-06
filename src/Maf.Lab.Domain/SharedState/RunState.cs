namespace Maf.Lab.Domain.SharedState;

/// <summary>How a run ended, or that it has not.</summary>
public static class RunOutcomes
{
    public const string Running = "running";
    public const string Answered = "answered";
    public const string AwaitingPerson = "awaiting_person";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";

    /// <summary>The outcomes a run ends with; awaiting a person is a pause, not an end.</summary>
    public static bool IsTerminal(string outcome) => outcome is Answered or Failed or Cancelled;
}

/// <summary>One tool call of a run, as a client that was away needs to see it: what, and how it went.</summary>
public sealed record RunToolCall(string CallId, string ToolName, string ArgumentSummary, string? ResultSummary, bool Finished, bool IsError);

/// <summary>
/// Where a run stands, readable by every replica. It is a snapshot and not a recording: a client that comes back
/// is told what the turn has said and done, not the frames it missed.
/// </summary>
/// <param name="Outcome">One of <see cref="RunOutcomes"/>.</param>
/// <param name="AwaitingId">The interrupt a paused run is waiting on, so the client can ask about it.</param>
/// <param name="Instance">
/// The process that owns the run (introduce-plugins decision 2), unique per process start. A reader that finds the run
/// still running while this process has no heartbeat marks it cancelled. Null in states written before it existed.
/// </param>
public sealed record RunState(
    string RunId,
    string ConversationId,
    string UserId,
    string TenantId,
    string Answer,
    IReadOnlyList<RunToolCall> ToolCalls,
    string Outcome,
    string? AwaitingId,
    string? TurnId,
    string? Error,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt,
    string? Instance = null)
{
    /// <summary>True once the run has ended; a terminal state is never overwritten by another outcome.</summary>
    public bool IsTerminal => RunOutcomes.IsTerminal(Outcome);
}
