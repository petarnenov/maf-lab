namespace Maf.Lab.TestGen;

/// <summary>
/// A request to the coverage runner: build and test one toolchain at one commit, optionally with a diff applied,
/// optionally reporting one file. <see cref="Tests"/> chooses the tests: <see cref="TestScope.All"/> (the default) or
/// <see cref="TestScope.Related"/>, which needs the target file. The runner answers with a job; the caller polls it
/// until it is done.
/// </summary>
public sealed record RunnerRequest(string Commit, string Toolchain, string? Diff = null, string? TargetFile = null,
    string? Tests = null);

/// <summary>Which tests a runner job runs.</summary>
public static class TestScope
{
    /// <summary>The toolchain's whole unit suite.</summary>
    public const string All = "all";

    /// <summary>The test files the diff adds or changes, and the tests that use the target or one of them.</summary>
    public const string Related = "related";
}

/// <summary>
/// What a job ran: the scope it used, the test files of a related run (at most <see cref="MaxFiles"/>), and why it ran
/// the whole suite when related tests were asked for.
/// </summary>
public sealed record TestSelection(string Scope, IReadOnlyList<string> TestFiles, string? Reason = null)
{
    public const int MaxFiles = 50;

    public static readonly TestSelection Whole = new(TestScope.All, []);

    public static TestSelection FellBack(string reason) => new(TestScope.All, [], reason);
}

/// <summary>A file's executable lines as one run measured them: those it ran, and those it did not.</summary>
public sealed record LineHits(IReadOnlyList<int> Covered, IReadOnlyList<int> Uncovered);

/// <summary>A runner job as the caller sees it while it waits.</summary>
public sealed record RunnerJob(string Id, string State, int QueuePosition, RunnerResult? Result);

public static class RunnerJobState
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Done = "done";
}

/// <summary>
/// What one runner job found. <see cref="Status"/> says whether it got as far as measuring:
/// ok | diff_rejected | timed_out | restore_failed | checkout_failed | error.
/// </summary>
public sealed record RunnerResult(
    string Status,
    string Build,
    IReadOnlyList<string> Diagnostics,
    TestCounts Tests,
    IReadOnlyList<TestFailure> Failures,
    string? CoberturaXml,
    string? MeasuredRoot,
    double? TargetPct,
    IReadOnlyList<int[]> Uncovered,
    long DurationMs,
    TestSelection? Selection = null,
    LineHits? TargetLines = null)
{
    public bool Measured => Status == RunnerStatus.Ok && Build == BuildOutcome.Ok;
    public bool Green => Measured && Tests.Failed == 0;

    /// <summary>Only some of the tests ran: the target's coverage is what they covered.</summary>
    public bool Focused => Selection?.Scope == TestScope.Related;

    public static RunnerResult Failed(string status, long durationMs, IReadOnlyList<string>? diagnostics = null) =>
        new(status, BuildOutcome.Skipped, diagnostics ?? [], new TestCounts(0, 0, 0), [], null, null, null, [], durationMs);
}

public sealed record TestCounts(int Passed, int Failed, int Skipped);

public sealed record TestFailure(string Name, string Message);

public static class RunnerStatus
{
    public const string Ok = "ok";
    public const string DiffRejected = "diff_rejected";
    public const string TimedOut = "timed_out";
    public const string RestoreFailed = "restore_failed";
    public const string CheckoutFailed = "checkout_failed";
    public const string Error = "error";
}

public static class BuildOutcome
{
    public const string Ok = "ok";
    public const string Failed = "failed";
    public const string Skipped = "skipped";
}

/// <summary>
/// The diagnostics the runner adds when a diff's files miss the lint bar CI applies (coverage-runner): a build
/// warning as the build printed it (<c>path(line,col): warning CODE: …</c>), an ESLint error
/// (<c>path(line,col): eslint RULE: …</c>) or a Prettier difference (<c>path(line): prettier: …</c>). One shape,
/// so the agent can say these fail the build as in CI, and the api can tell a lint-only failure from a compile error.
/// </summary>
public static partial class LintDiagnostics
{
    public static string Eslint(string path, int line, int column, string? rule, string message) =>
        $"{path}({line},{column}): eslint {rule ?? "error"}: {message}";

    public static string Prettier(string path, int? line, string message) =>
        $"{path}{(line is { } l ? $"({l})" : "")}: prettier: {message}";

    /// <summary>The lint check itself could not run: the build fails rather than pass a check that did not happen.</summary>
    public static string CouldNotRun(string tool, string why) => $"web: {tool}: could not run: {why}";

    public static bool Is(string diagnostic) => Shape().IsMatch(diagnostic);

    /// <summary>The build failed on lint diagnostics alone: the code compiled and its tests ran.</summary>
    public static bool OnlyLint(RunnerResult result) =>
        result is { Status: RunnerStatus.Ok, Build: BuildOutcome.Failed, Diagnostics.Count: > 0 } && result.Diagnostics.All(Is);

    [System.Text.RegularExpressions.GeneratedRegex(@"^[^\s(:]+(\(\d+(,\d+)?\))?: (warning [A-Za-z]+\d+:|eslint[ :]|prettier:)")]
    private static partial System.Text.RegularExpressions.Regex Shape();
}
