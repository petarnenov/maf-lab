using System.ComponentModel;
using System.Text;
using Maf.Lab.TestGen;
using Microsoft.Extensions.AI;

namespace Maf.Lab.TestAgent;

// What the model is given back. Purpose-built, small, and never an entity or a host path.
public sealed record FileText(string Path, int Lines, string Text, bool Truncated);
public sealed record DirectoryEntry(string Path, string Kind);
public sealed record Listing(IReadOnlyList<DirectoryEntry> Entries, bool Truncated);
public sealed record Written(string Path, bool Created, int Bytes);
public sealed record TestRun(string Status, string Build, IReadOnlyList<string> Diagnostics, TestCounts Tests,
    IReadOnlyList<TestFailure> Failures, double? TargetPct, IReadOnlyList<int[]> Uncovered, string? Note);
public sealed record Coverage(string Path, double? Pct, IReadOnlyList<int[]> Uncovered);
public sealed record BugRecorded(bool Recorded, int Count, string? Note, int Limit);

/// <summary>
/// The agent's six tools, bound to one task's workspace. Every path goes through <see cref="WorkspacePaths"/>:
/// reading anywhere in the repository, writing only to the toolchain's test locations. A refusal is a tool error the
/// model can act on, never the end of the run.
/// </summary>
public sealed class TestAgentTools(Workspace workspace, TestGenRequest request, CoverageRunnerClient runner)
{
    public const int MaxReadLines = 2_000;
    public const int MaxEntries = 500;
    public const int MaxWriteBytes = 200_000;

    private readonly List<SuspectedBug> _bugs = [];
    private RunnerResult? _last;
    private int _runsThisAttempt;

    public IReadOnlyList<SuspectedBug> SuspectedBugs => _bugs;

    public IList<AITool> All() =>
    [
        AIFunctionFactory.Create(ReadFile, "read_file"),
        AIFunctionFactory.Create(ListFiles, "list_files"),
        AIFunctionFactory.Create(WriteFile, "write_file"),
        AIFunctionFactory.Create(RunTests, "run_tests"),
        AIFunctionFactory.Create(ReadCoverage, "read_coverage"),
        AIFunctionFactory.Create(ReportSuspectedBug, "report_suspected_bug"),
    ];

    /// <summary>Starts a new attempt: the model's own test runs are counted per attempt.</summary>
    public void BeginAttempt() => _runsThisAttempt = 0;

    /// <summary>The attempt's measured run, so read_coverage answers from it in the next attempt.</summary>
    public void Measured(RunnerResult result) => _last = result;

    [Description("Read a file of the repository (as of the commit, plus any test file you have written). Returns at most 2000 lines.")]
    public async Task<FileText> ReadFile([Description("Path relative to the repository root, forward slashes.")] string path,
        CancellationToken ct)
    {
        var full = WorkspacePaths.Resolve(workspace.Root, path, PathAccess.Read, request.Toolchain);
        if (!File.Exists(full))
        {
            throw new PathRefusedException("No such file.");
        }
        var lines = await File.ReadAllLinesAsync(full, ct);
        var truncated = lines.Length > MaxReadLines;
        return new FileText(WorkspacePaths.Relative(path), lines.Length, string.Join('\n', lines.Take(MaxReadLines)), truncated);
    }

    [Description("List files and folders under a folder of the repository, optionally filtered by a file-name pattern such as *.cs.")]
    public Listing ListFiles([Description("Folder relative to the repository root; empty for the root.")] string dir,
        [Description("Optional pattern for file names, e.g. *.test.ts.")] string? glob = null)
    {
        var full = string.IsNullOrWhiteSpace(dir) ? workspace.Root : WorkspacePaths.Resolve(workspace.Root, dir, PathAccess.Read, request.Toolchain);
        if (!Directory.Exists(full))
        {
            throw new PathRefusedException("No such folder.");
        }
        var entries = new List<DirectoryEntry>();
        foreach (var sub in Directory.EnumerateDirectories(full).Order())
        {
            var name = Path.GetFileName(sub);
            if (name is ".git" or "node_modules" or "bin" or "obj")
            {
                continue;
            }
            entries.Add(new(Rel(sub), "dir"));
        }
        foreach (var file in Directory.EnumerateFiles(full, string.IsNullOrWhiteSpace(glob) ? "*" : glob).Order())
        {
            var rel = Rel(file);
            if (WorkspacePaths.IsReadable(rel))
            {
                entries.Add(new(rel, "file"));
            }
        }
        return new Listing(entries.Take(MaxEntries).ToList(), entries.Count > MaxEntries);
    }

    [Description("Create or replace a test file. Only test locations may be written: under tests/ for dotnet; *.test.ts, *.test.tsx or web/src/test/ for vitest. Production code is read-only.")]
    public async Task<Written> WriteFile([Description("Path of the test file, relative to the repository root.")] string path,
        [Description("The complete new content of the file.")] string content, CancellationToken ct)
    {
        var full = WorkspacePaths.Resolve(workspace.Root, path, PathAccess.Write, request.Toolchain);
        var bytes = Encoding.UTF8.GetByteCount(content);
        if (bytes > MaxWriteBytes)
        {
            throw new PathRefusedException("That file is too large for a test.");
        }
        var created = !File.Exists(full);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllTextAsync(full, content, ct);
        return new Written(WorkspacePaths.Relative(path), created, bytes);
    }

    [Description("Build and run the tests with your changes and measure the target file's coverage. Only as often per attempt as its instructions say; the attempt is measured again when you finish.")]
    public async Task<TestRun> RunTests(CancellationToken ct)
    {
        if (++_runsThisAttempt > request.TestRuns)
        {
            throw new PathRefusedException("You have run the tests as often as one attempt allows; finish the attempt to have it measured.");
        }
        var result = await runner.RunAsync(new RunnerRequest(workspace.Commit, request.Toolchain, await workspace.DiffAsync(ct), request.TargetFile), ct);
        _last = result;
        return Summary(result);
    }

    [Description("Coverage of the target file from the last test run: its line percentage and the line ranges still uncovered.")]
    public Coverage ReadCoverage([Description("The target file's path.")] string path)
    {
        if (WorkspacePaths.Relative(path) != request.TargetFile)
        {
            throw new PathRefusedException("Coverage is reported for the target file only.");
        }
        return new Coverage(request.TargetFile, _last?.TargetPct, _last?.Uncovered ?? []);
    }

    [Description("Report that a test you wrote shows the production code does not behave as intended. Keep the test and its assertion, skip it with the reason 'suspected-bug: <title>', and never change production code or the assertion to make it pass. At most as many per run as the instructions say.")]
    public BugRecorded ReportSuspectedBug(
        [Description("The test file, relative to the repository root.")] string testFile,
        [Description("The test's name: the method name for xUnit, the test's title for Vitest.")] string test,
        [Description("A short title for the bug, the same text as in the skip reason.")] string title,
        [Description("What is wrong and why you believe the code, not the test, is at fault (names, documentation, callers).")] string description,
        [Description("What the code should do.")] string expected,
        [Description("What it does instead.")] string actual,
        [Description("The failure message of the test run.")] string failure)
    {
        var file = WorkspacePaths.Relative(testFile);
        _bugs.RemoveAll(b => b.TestFile == file && b.Test == test);
        if (_bugs.Count >= request.SuspectedBugLimit)
        {
            return new BugRecorded(false, _bugs.Count,
                $"This run reports at most {request.SuspectedBugLimit} suspected bug{(request.SuspectedBugLimit == 1 ? "" : "s")}; this one was not recorded.", request.SuspectedBugLimit);
        }
        _bugs.Add(new SuspectedBug(file, test, Clip(title, 120), Clip(description, 2_000), Clip(expected, 500), Clip(actual, 500),
            Clip(failure, 2_000)));
        return new BugRecorded(true, _bugs.Count, null, request.SuspectedBugLimit);
    }

    public static TestRun Summary(RunnerResult r) => new(r.Status, r.Build, r.Diagnostics.Take(30).ToList(), r.Tests,
        r.Failures.Take(20).ToList(), r.TargetPct, r.Uncovered,
        r.Status == RunnerStatus.DiffRejected ? "Your changes could not be applied to the commit." : null);

    private string Rel(string full) => Path.GetRelativePath(workspace.Root, full).Replace('\\', '/');

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..max];
}

/// <summary>
/// A tool call as the run's activity shows it: which tool, the path it concerned, and a one-line summary read from
/// the tool's result — counts, sizes, outcomes, never the text of a file.
/// </summary>
public static class ToolSummaries
{
    private const int MaxPath = 300;

    public static ToolActivity Of(string name, IEnumerable<KeyValuePair<string, object?>> arguments, object? result, string outcome,
        string? problem)
    {
        var args = arguments.ToDictionary(a => a.Key, a => a.Value, StringComparer.OrdinalIgnoreCase);
        var path = Arg(args, "path") ?? Arg(args, "testFile") ?? Arg(args, "dir");
        if (outcome != ToolOutcome.Ok)
        {
            return new ToolActivity(name, path, outcome, problem ?? "The tool failed.");
        }
        var r = result is System.Text.Json.JsonElement e ? e : System.Text.Json.JsonSerializer.SerializeToElement(result, TestGenKinds.Json);
        var summary = name switch
        {
            "read_file" => $"{Int(r, "lines")} lines{(Bool(r, "truncated") ? " (truncated)" : "")}",
            "list_files" => $"{Count(r, "entries")} entries{(Bool(r, "truncated") ? " (truncated)" : "")}",
            "write_file" => $"{(Bool(r, "created") ? "created" : "updated")}, {Int(r, "bytes")} bytes",
            "run_tests" => RunSummary(r),
            "read_coverage" => $"{Pct(r, "pct")}, {Count(r, "uncovered")} uncovered ranges",
            "report_suspected_bug" => Bool(r, "recorded") ? $"recorded ({Int(r, "count")} of {Int(r, "limit")})" : Str(r, "note") ?? "not recorded",
            _ => "done",
        };
        return new ToolActivity(name, path, outcome, summary);
    }

    private static string RunSummary(System.Text.Json.JsonElement r)
    {
        var tests = Prop(r, "tests");
        var text = $"build {Str(r, "build") ?? "?"}, {(tests is { } t ? Int(t, "passed") : 0)} passed, "
            + $"{(tests is { } f ? Int(f, "failed") : 0)} failed, {Pct(r, "targetPct")}";
        return Str(r, "note") is { Length: > 0 } note ? $"{text} — {note}" : text;
    }

    private static string? Arg(Dictionary<string, object?> args, string key) =>
        args.TryGetValue(key, out var value) ? Clip(value switch
        {
            string s => s,
            System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.String } j => j.GetString(),
            _ => null,
        }) : null;

    private static string? Clip(string? text) => text is null ? null : text.Length <= MaxPath ? text : text[..MaxPath];

    private static System.Text.Json.JsonElement? Prop(System.Text.Json.JsonElement e, string name)
    {
        if (e.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            return null;
        }
        foreach (var p in e.EnumerateObject())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return p.Value;
            }
        }
        return null;
    }

    private static int Int(System.Text.Json.JsonElement e, string name) =>
        Prop(e, name) is { ValueKind: System.Text.Json.JsonValueKind.Number } v && v.TryGetInt32(out var i) ? i : 0;

    private static bool Bool(System.Text.Json.JsonElement e, string name) =>
        Prop(e, name) is { ValueKind: System.Text.Json.JsonValueKind.True };

    private static string? Str(System.Text.Json.JsonElement e, string name) =>
        Prop(e, name) is { ValueKind: System.Text.Json.JsonValueKind.String } v ? v.GetString() : null;

    private static int Count(System.Text.Json.JsonElement e, string name) =>
        Prop(e, name) is { ValueKind: System.Text.Json.JsonValueKind.Array } v ? v.GetArrayLength() : 0;

    private static string Pct(System.Text.Json.JsonElement e, string name) =>
        Prop(e, name) is { ValueKind: System.Text.Json.JsonValueKind.Number } v
            ? v.GetDouble().ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "%"
            : "not measured";
}
