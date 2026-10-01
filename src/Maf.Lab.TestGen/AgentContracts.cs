using System.Text.Json;
using System.Text.Json.Serialization;

namespace Maf.Lab.TestGen;

/// <summary>
/// The test agent's A2A contract: one data part in, progress data parts while it works, one report artifact at the
/// end. Each carries <c>kind</c>, so a reader can tell the three apart and a version change is visible.
/// </summary>
public static class TestGenKinds
{
    public const string Request = "testgen.request/v1";
    public const string Progress = "testgen.progress/v1";
    public const string Report = "testgen.report/v1";
    public const string ReportArtifact = "testgen-report";
    public const string Activity = "testgen.activity/v1";
    /// <summary>The name of every activity artifact; each batch is its own artifact, so a resubscription sees it.</summary>
    public const string ActivityArtifact = "testgen-activity";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

/// <summary>The run's caps. A cap left null is unlimited in that dimension; with neither set, only the attempt cap, a
/// cancel or the caller's deadline ends the run early.</summary>
public sealed record TestGenBudget(long? MaxTokens = null, double? MaxCostUsd = null)
{
    public static readonly TestGenBudget Unlimited = new();

    public bool IsUnlimited => MaxTokens is null && MaxCostUsd is null;

    /// <summary>What is wrong with the caps, in words for the caller; null when they can be used.</summary>
    public string? Problem() =>
        MaxTokens is <= 0 ? "budget.maxTokens must be positive when set."
        : MaxCostUsd is { } cost && !(cost > 0) ? "budget.maxCostUsd must be positive when set."
        : null;
}

/// <summary>The model's price, sent with the task so the agent can count cost the way the picker estimated it.</summary>
public sealed record ModelPrice(double InputPerMTok, double OutputPerMTok);

public sealed record TestGenRequest(
    string Kind,
    string RunId,
    string Commit,
    string TargetFile,
    string Toolchain,
    int TargetLinePct,
    int MaxAttempts,
    string Model,
    ModelPrice Price,
    TestGenBudget? Budget,
    int? ToolRoundsPerAttempt = null,
    int? TestRunsPerAttempt = null,
    int? MaxSuspectedBugs = null)
{
    /// <summary>The attempt cap, <see cref="RunLimits.Attempts"/>' maximum. The api asks for this many by default; the agent refuses more.</summary>
    public const int AttemptLimit = RunLimits.MaxAttempts;

    /// <summary>The tool rounds each attempt has: the task's, or the default when it names none.</summary>
    [JsonIgnore]
    public int ToolRounds => ToolRoundsPerAttempt ?? RunLimits.ToolRoundsPerAttempt.Default;

    /// <summary>How often the model may run the tests itself in one attempt: the task's, or the default.</summary>
    [JsonIgnore]
    public int TestRuns => TestRunsPerAttempt ?? RunLimits.TestRunsPerAttempt.Default;

    /// <summary>How many suspected bugs the run may report: the task's, or the default.</summary>
    [JsonIgnore]
    public int SuspectedBugLimit => MaxSuspectedBugs ?? RunLimits.SuspectedBugs.Default;

    /// <summary>What is wrong with the request, in words for the caller; null when it can be worked on.</summary>
    public string? Problem()
    {
        if (Kind != TestGenKinds.Request) return $"kind must be {TestGenKinds.Request}.";
        if (!Git.IsCommitId(Commit) || Commit.Length != 40) return "commit must be a full 40-character commit id.";
        if (Toolchain is not ("dotnet" or "vitest")) return "toolchain must be dotnet or vitest.";
        if (string.IsNullOrWhiteSpace(TargetFile)) return "targetFile is required.";
        if (TargetLinePct is < 1 or > 100) return "targetLinePct must be from 1 to 100.";
        if (RunLimits.Attempts.Problem("maxAttempts", MaxAttempts) is { } attempts) return attempts;
        if (RunLimits.ToolRoundsPerAttempt.Problem("toolRoundsPerAttempt", ToolRoundsPerAttempt) is { } rounds) return rounds;
        if (RunLimits.TestRunsPerAttempt.Problem("testRunsPerAttempt", TestRunsPerAttempt) is { } runs) return runs;
        if (RunLimits.SuspectedBugs.Problem("maxSuspectedBugs", MaxSuspectedBugs) is { } bugs) return bugs;
        if (string.IsNullOrWhiteSpace(Model)) return "model is required.";
        if (Budget?.Problem() is { } budget) return budget;
        if (Price is null || Price.InputPerMTok < 0 || Price.OutputPerMTok < 0) return "price must not be negative.";
        return null;
    }
}

public static class AttemptPhase
{
    public const string Generating = "generating";
    public const string Building = "building";
    public const string Testing = "testing";
    public const string Measuring = "measuring";
}

public sealed record TestGenProgress(
    string Kind, int Attempt, int MaxAttempts, string Phase, double? LastLinePct, long Tokens, double CostUsd);

/// <summary>What an activity entry records.</summary>
public static class ActivityType
{
    public const string Phase = "phase";
    public const string Tool = "tool";
    public const string Attempt = "attempt";
    public const string Text = "text";
    public const string Reasoning = "reasoning";
    public const string Stopped = "stopped";

    /// <summary>The agent took the task over after a restart; the entry's attempt is the one it resumes at.</summary>
    public const string Resumed = "resumed";
}

public static class ToolOutcome
{
    public const string Ok = "ok";
    public const string Refused = "refused";
    public const string Failed = "failed";
}

/// <summary>A tool call as the activity shows it: the tool, the path it concerned, and a summary — never file text.</summary>
public sealed record ToolActivity(string Name, string? Path, string Outcome, string Summary);

/// <summary>
/// What one runner job of an attempt ran: the scope it used, how many related test files (at most
/// <see cref="TestSelection.MaxFiles"/>), how many tests, why it ran the whole suite when related tests were asked for,
/// and whether the runner answered with a result it had computed earlier. <see cref="Pct"/> is the coverage that run
/// measured (set on a confirmation).
/// </summary>
public sealed record AttemptRun(string Scope, int Files, int Tests, string? Reason = null, bool Reused = false, double? Pct = null)
{
    /// <summary>The run as the attempt saw it; <paramref name="reason"/> stands in when the runner gave none.</summary>
    public static AttemptRun Of(RunnerResult result, string? reason = null, bool withPct = false)
    {
        var selection = result.Selection ?? TestSelection.Whole;
        return new AttemptRun(selection.Scope, selection.Scope == TestScope.Related ? selection.TestFiles.Count : 0,
            result.Tests.Passed + result.Tests.Failed + result.Tests.Skipped, selection.Reason ?? reason, result.ReusedFrom is not null,
            withPct ? result.TargetPct : null);
    }
}

/// <summary>
/// An attempt's result as the activity shows it. <see cref="Run"/> is what its measured run ran; <see cref="Confirmation"/>
/// the whole-suite run that confirmed it, whose numbers are then the attempt's. Both are absent on entries recorded
/// before they existed.
/// </summary>
public sealed record AttemptActivity(double? Before, double? After, string Build, TestCounts Tests, IReadOnlyList<string> Errors,
    int Violations, AttemptRun? Run = null, AttemptRun? Confirmation = null);

/// <summary>
/// Why the agent's work on a task is over: the last entry of a completed task. <see cref="NotStarted"/> names the
/// attempt a budget stop did not start.
/// </summary>
public sealed record StoppedActivity(string Reason, int LastAttempt, double? BestPct, int? NotStarted = null);

/// <summary>
/// One thing the agent did, in order (<see cref="Seq"/> increases within a task). Model text and reasoning stream as
/// chunks: the first chunk is an entry of its own, each later one names the entry it <see cref="Continues"/>. Only
/// the api's activity record and the browser ever see this; it is never logged or traced.
/// </summary>
public sealed record TestGenActivity(
    string Kind,
    long Seq,
    DateTimeOffset At,
    int Attempt,
    string Type,
    string? Phase = null,
    ToolActivity? Tool = null,
    AttemptActivity? Result = null,
    long? Continues = null,
    string? Text = null,
    bool Truncated = false,
    StoppedActivity? Stop = null)
{
    /// <summary>The most text one entry holds, however many chunks it streamed in.</summary>
    public const int MaxTextBytes = 4 * 1024;

    /// <summary>The most error lines an attempt entry carries.</summary>
    public const int MaxErrors = 10;
}

public static class StopReason
{
    public const string Target = "target";
    public const string Attempts = "attempts";
    public const string Budget = "budget";
    public const string Error = "error";
    public const string Canceled = "canceled";
}

public sealed record AttemptLog(
    int N,
    double? Before,
    double? After,
    string Build,
    TestCounts Tests,
    IReadOnlyList<string> TestsAdded,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> GuardrailViolations,
    IReadOnlyList<int[]> Uncovered,
    AttemptRun? Run = null,
    AttemptRun? Confirmation = null);

/// <summary>
/// A test that shows the production code does not do what it is meant to. The test keeps its assertion and is
/// skipped with a suspected-bug marker; the api proves the failure and opens an issue.
/// </summary>
public sealed record SuspectedBug(
    string TestFile, string Test, string Title, string Description, string Expected, string Actual, string Failure)
{
    /// <summary>The most a run may report, and the default; a task may set fewer (<see cref="RunLimits.SuspectedBugs"/>).</summary>
    public const int MaxPerRun = 3;
    public const string Marker = "suspected-bug";

    /// <summary>The marker as the agent writes it, before the api adds the issue link.</summary>
    public string SkipReason => $"{Marker}: {Title}";
}

public sealed record TestGenUsage(long InputTokens, long OutputTokens, double EstimatedCostUsd);

public sealed record TestGenReport(
    string Kind,
    bool GoalReached,
    string StopReason,
    int Target,
    double? Baseline,
    double? Final,
    IReadOnlyList<AttemptLog> Attempts,
    TestGenUsage Usage,
    string Diff,
    IReadOnlyList<SuspectedBug>? SuspectedBugs = null,
    VerificationRun? Verification = null);

/// <summary>
/// How the api's verification run was obtained (set by the api, never by the agent): its scope, its counts and coverage,
/// and, when the runner answered with a result it had already computed for the identical request (the agent's
/// whole-suite confirmation), which job computed it.
/// </summary>
public sealed record VerificationRun(string Scope, TestCounts Tests, double? Pct, RunnerReuse? ReusedFrom = null);

/// <summary>Why a task failed, as the status message's code: short, stable, and free of detail.</summary>
public static class TestGenFailure
{
    public const string ModelUnavailable = "model_unavailable";
    public const string CheckoutFailed = "checkout_failed";
    public const string RunnerUnavailable = "runner_unavailable";
    public const string DiffTooLarge = "diff_too_large";
    public const string Internal = "internal_error";

    /// <summary>The agent restarted and the task had nothing to resume from.</summary>
    public const string Interrupted = "interrupted";

    /// <summary>Largest diff the report carries; it keeps the whole artifact well under the 1 MB request limit.</summary>
    public const int MaxDiffBytes = 256 * 1024;
}
