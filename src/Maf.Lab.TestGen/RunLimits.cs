namespace Maf.Lab.TestGen;

/// <summary>A per-run limit's range and the value a run gets when none is chosen.</summary>
public sealed record LimitBounds(int Min, int Max, int Default)
{
    public bool Allows(int value) => value >= Min && value <= Max;

    /// <summary>What is wrong with a chosen value, in words for the caller; null when it is absent or within bounds.</summary>
    public string? Problem(string name, int? value) =>
        value is { } v && !Allows(v) ? $"{name} must be from {Min} to {Max}." : null;
}

/// <summary>
/// The limits a test-generation run may set, and their defaults: the one place they are set. The api serves them to the
/// picker and validates a start against them; the agent validates the task against them and enforces what it carries.
/// Each default is its maximum, so a run can be made smaller than today's, never larger.
/// </summary>
public static class RunLimits
{
    public const int MaxAttempts = 10;

    public static readonly LimitBounds Attempts = new(1, MaxAttempts, MaxAttempts);

    /// <summary>Model round-trips (each possibly calling tools) in one attempt. Room to read neighbours first (DECISIONS.md §60).</summary>
    public static readonly LimitBounds ToolRoundsPerAttempt = new(1, 40, 40);

    /// <summary>How often the model may run the tests itself within one attempt; the attempt's own run is extra.</summary>
    public static readonly LimitBounds TestRunsPerAttempt = new(0, 2, 2);

    /// <summary>Suspected bugs a run may report; 0 means none. The run's deadline is the api's (its configuration owns it).</summary>
    public static readonly LimitBounds SuspectedBugs = new(0, SuspectedBug.MaxPerRun, SuspectedBug.MaxPerRun);
}
