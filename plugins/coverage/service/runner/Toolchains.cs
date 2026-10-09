using System.Text.Json;
using System.Text.RegularExpressions;
using Maf.Lab.TestGen;

namespace Maf.Lab.CoverageRunner;

/// <summary>
/// What a toolchain run found, before the target file is looked at. <see cref="TestFiles"/> is the test files the
/// toolchain itself reported running, where it reports them (Vitest). <see cref="Warnings"/>: the build warnings it
/// printed, in the diagnostics' form, which the lint bar holds a diff's files to (<see cref="LintBar"/>).
/// </summary>
public sealed record ToolchainOutcome(string Build, IReadOnlyList<string> Diagnostics, TestCounts Tests,
    IReadOnlyList<TestFailure> Failures, string? CoberturaPath, bool TimedOut, IReadOnlyList<string>? TestFiles = null,
    IReadOnlyList<string>? Warnings = null)
{
    /// <summary>It built and finished, and not one test ran.</summary>
    public bool RanNoTest => Build == BuildOutcome.Ok && !TimedOut && Tests is { Passed: 0, Failed: 0, Skipped: 0 };
}

/// <summary>Builds and runs one toolchain's unit tests, all or the planned ones, with coverage in a prepared workspace.</summary>
public interface IToolchainRunner
{
    string Toolchain { get; }

    Task<ToolchainOutcome> RunAsync(string workspace, string outputDir, TestPlan plan, TimeSpan timeLimit, CancellationToken ct);
}

/// <summary>
/// The .NET unit tests (not the integration tests, which need containers the runner cannot start), with the
/// Microsoft.Testing.Platform coverage extension writing Cobertura to a folder the tests do not know about.
/// </summary>
public sealed partial class DotnetToolchain(RunnerOptions options) : IToolchainRunner
{
    public string Toolchain => "dotnet";

    public async Task<ToolchainOutcome> RunAsync(string workspace, string outputDir, TestPlan plan, TimeSpan timeLimit, CancellationToken ct)
    {
        var report = Path.Combine(outputDir, "dotnet.cobertura.xml");
        var outcome = await ChildProcess.RunAsync("dotnet", Arguments(options, report, plan), workspace, timeLimit, ct);
        return Parse(outcome, workspace, File.Exists(report) ? report : null);
    }

    /// <summary>`dotnet test` over the unit test project, with one <c>--filter-class</c> per planned class.</summary>
    public static IReadOnlyList<string> Arguments(RunnerOptions options, string report, TestPlan plan)
    {
        List<string> args =
        [
            "test", "--project", options.DotnetTestProject, "--", "--coverage", "--coverage-output-format", "cobertura",
            "--coverage-settings", options.DotnetCoverageSettings, "--coverage-output", report,
        ];
        foreach (var name in plan.Related ? plan.Filters : [])
        {
            args.AddRange(["--filter-class", name]);
        }
        return args;
    }

    /// <summary>Reads `dotnet test` console output: compiler errors, the summary, and each failed test's message.</summary>
    public static ToolchainOutcome Parse(ProcessOutcome outcome, string workspace, string? report)
    {
        var text = outcome.Output.Replace(workspace.TrimEnd('/') + "/", "", StringComparison.Ordinal);
        var diagnostics = CompilerError().Matches(text).Select(m => ProjectSuffix().Replace(m.Value.Trim(), "")).Distinct().Take(50).ToList();
        var warnings = CompilerWarning().Matches(text).Select(m => ProjectSuffix().Replace(m.Value.Trim(), "")).Distinct().ToList();
        var buildFailed = diagnostics.Count > 0 || text.Contains("Build failed", StringComparison.Ordinal);

        var failures = new List<TestFailure>();
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (FailedTest().Match(lines[i]) is { Success: true } m)
            {
                var message = lines.Skip(i + 1).Take(6).TakeWhile(l => l.StartsWith("  ", StringComparison.Ordinal) && l.Trim().Length > 0)
                    .Select(l => l.Trim());
                failures.Add(new TestFailure(m.Groups[1].Value, string.Join(" ", message)));
            }
        }
        // A run that names failed tests failed, whatever its summary says or whether one was printed at all. Without
        // a summary, nothing says every test ran and passed: the run counts as failed, never as zero failures.
        var summarised = Summary("total").IsMatch(text);
        var failed = Math.Max(Last(text, "failed"), failures.Count);
        if (!buildFailed && !summarised && failed == 0)
        {
            failed = 1;
            failures.Add(new TestFailure("(test run)", "The test run printed no summary, so it is not known that every test passed."));
        }
        var tests = new TestCounts(Last(text, "succeeded"), failed, Last(text, "skipped"));
        return new ToolchainOutcome(buildFailed ? BuildOutcome.Failed : BuildOutcome.Ok, diagnostics, tests,
            failures.Take(50).ToList(), buildFailed ? null : report, outcome.TimedOut, Warnings: warnings);
    }

    private static int Last(string text, string name) =>
        Summary(name).Matches(text).LastOrDefault() is { } m ? int.Parse(m.Groups[1].Value) : 0;

    private static Regex Summary(string name) => new($@"^\s*{name}:\s*(\d+)\s*$", RegexOptions.Multiline);

    // Analyzer ids are not all upper case (xUnit1031), so neither pattern insists on it.
    [GeneratedRegex(@"^[^\n]*: error [A-Za-z]+\d+: [^\n]*", RegexOptions.Multiline)]
    private static partial Regex CompilerError();

    [GeneratedRegex(@"^[^\n]*: warning [A-Za-z]+\d+: [^\n]*", RegexOptions.Multiline)]
    private static partial Regex CompilerWarning();

    [GeneratedRegex(@"\s*\[[^\]]*\.csproj\]$")]
    private static partial Regex ProjectSuffix();

    [GeneratedRegex(@"^\s*failed (\S+) \(")]
    private static partial Regex FailedTest();
}

/// <summary>The web unit tests with Vitest's v8 coverage, reported as JSON and Cobertura.</summary>
public sealed class VitestToolchain(RunnerOptions options) : IToolchainRunner
{
    public string Toolchain => "vitest";

    public async Task<ToolchainOutcome> RunAsync(string workspace, string outputDir, TestPlan plan, TimeSpan timeLimit, CancellationToken ct)
    {
        var web = Path.Combine(workspace, "web");
        var modules = Path.Combine(web, "node_modules");
        if (!Directory.Exists(modules) && options.WebNodeModules is { Length: > 0 } prepared)
        {
            Directory.CreateSymbolicLink(modules, prepared);
        }
        var json = Path.Combine(outputDir, "vitest.json");
        var outcome = await ChildProcess.RunAsync(Path.Combine(modules, ".bin", "vitest"), Arguments(outputDir, json, plan),
            web, timeLimit, ct);
        var report = Path.Combine(outputDir, "cobertura-coverage.xml");
        return Parse(outcome, File.Exists(json) ? await File.ReadAllTextAsync(json, ct) : null, workspace, File.Exists(report) ? report : null);
    }

    /// <summary>`vitest run` for the whole suite; `vitest related --run` with the planned files for related tests.</summary>
    public static IReadOnlyList<string> Arguments(string outputDir, string json, TestPlan plan) =>
    [
        .. plan.Related ? ["related", "--run", "--passWithNoTests", .. plan.Filters] : new[] { "run" },
        "--coverage", "--coverage.reportOnFailure=true", $"--coverage.reportsDirectory={outputDir}", "--reporter=json", $"--outputFile={json}",
    ];

    /// <summary>
    /// Reads Vitest's JSON report. A test file that does not even load (a syntax or type error) is the web's "build
    /// failed": it is reported as a diagnostic, not as a failing test.
    /// </summary>
    public static ToolchainOutcome Parse(ProcessOutcome outcome, string? json, string workspace, string? report)
    {
        if (json is null)
        {
            var lines = outcome.Output.Replace(workspace.TrimEnd('/') + "/", "", StringComparison.Ordinal)
                .Split('\n').Where(l => l.Contains("Error", StringComparison.Ordinal)).Take(20).ToList();
            return new ToolchainOutcome(BuildOutcome.Failed, lines, new TestCounts(0, 0, 0), [], null, outcome.TimedOut);
        }
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var diagnostics = new List<string>();
        var failures = new List<TestFailure>();
        var files = new List<string>();
        foreach (var file in root.GetProperty("testResults").EnumerateArray())
        {
            var name = Relative(file.GetProperty("name").GetString() ?? "", workspace);
            files.Add(name);
            var assertions = file.TryGetProperty("assertionResults", out var a) ? a.EnumerateArray().ToList() : [];
            if (file.GetProperty("status").GetString() == "failed" && assertions.Count == 0)
            {
                diagnostics.Add($"{name}: {Relative(file.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "", workspace)}");
            }
            foreach (var test in assertions.Where(t => t.GetProperty("status").GetString() == "failed"))
            {
                var message = test.GetProperty("failureMessages").EnumerateArray().Select(x => x.GetString() ?? "").FirstOrDefault() ?? "";
                failures.Add(new TestFailure(test.GetProperty("fullName").GetString() ?? name,
                    Relative(message.Split('\n').FirstOrDefault() ?? "", workspace)));
            }
        }
        var tests = new TestCounts(root.GetProperty("numPassedTests").GetInt32(), root.GetProperty("numFailedTests").GetInt32(),
            root.GetProperty("numPendingTests").GetInt32() + (root.TryGetProperty("numTodoTests", out var todo) ? todo.GetInt32() : 0));
        var build = diagnostics.Count > 0 ? BuildOutcome.Failed : BuildOutcome.Ok;
        return new ToolchainOutcome(build, diagnostics.Take(50).ToList(), tests, failures.Take(50).ToList(),
            build == BuildOutcome.Ok ? report : null, outcome.TimedOut, files.Order(StringComparer.Ordinal).ToList());
    }

    private static string Relative(string text, string workspace) => text.Replace(workspace.TrimEnd('/') + "/", "", StringComparison.Ordinal);
}
