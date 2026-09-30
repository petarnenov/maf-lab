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

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

public sealed record TestGenBudget(long MaxTokens, double MaxCostUsd);

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
    TestGenBudget Budget)
{
    public const int AttemptLimit = 5;

    /// <summary>What is wrong with the request, in words for the caller; null when it can be worked on.</summary>
    public string? Problem()
    {
        if (Kind != TestGenKinds.Request) return $"kind must be {TestGenKinds.Request}.";
        if (!Git.IsCommitId(Commit) || Commit.Length != 40) return "commit must be a full 40-character commit id.";
        if (Toolchain is not ("dotnet" or "vitest")) return "toolchain must be dotnet or vitest.";
        if (string.IsNullOrWhiteSpace(TargetFile)) return "targetFile is required.";
        if (TargetLinePct is < 1 or > 100) return "targetLinePct must be from 1 to 100.";
        if (MaxAttempts is < 1 or > AttemptLimit) return $"maxAttempts must be from 1 to {AttemptLimit}.";
        if (string.IsNullOrWhiteSpace(Model)) return "model is required.";
        if (Budget is null || Budget.MaxTokens <= 0 || Budget.MaxCostUsd <= 0) return "budget must set positive caps.";
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
    IReadOnlyList<int[]> Uncovered);

/// <summary>
/// A test that shows the production code does not do what it is meant to. The test keeps its assertion and is
/// skipped with a suspected-bug marker; the api proves the failure and opens an issue.
/// </summary>
public sealed record SuspectedBug(
    string TestFile, string Test, string Title, string Description, string Expected, string Actual, string Failure)
{
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
    IReadOnlyList<SuspectedBug>? SuspectedBugs = null);

/// <summary>Why a task failed, as the status message's code: short, stable, and free of detail.</summary>
public static class TestGenFailure
{
    public const string ModelUnavailable = "model_unavailable";
    public const string CheckoutFailed = "checkout_failed";
    public const string RunnerUnavailable = "runner_unavailable";
    public const string DiffTooLarge = "diff_too_large";
    public const string Internal = "internal_error";

    /// <summary>Largest diff the report carries; it keeps the whole artifact well under the 1 MB request limit.</summary>
    public const int MaxDiffBytes = 256 * 1024;
}
