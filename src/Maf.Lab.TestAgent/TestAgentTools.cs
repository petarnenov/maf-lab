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
public sealed record BugRecorded(bool Recorded, int Count, string? Note);

/// <summary>
/// The agent's six tools, bound to one task's workspace. Every path goes through <see cref="WorkspacePaths"/>:
/// reading anywhere in the repository, writing only to the toolchain's test locations. A refusal is a tool error the
/// model can act on, never the end of the run.
/// </summary>
public sealed class TestAgentTools(Workspace workspace, TestGenRequest request, CoverageRunnerClient runner, TestAgentOptions options)
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

    [Description("Build and run the tests with your changes and measure the target file's coverage. At most twice per attempt; the attempt is measured again when you finish.")]
    public async Task<TestRun> RunTests(CancellationToken ct)
    {
        if (++_runsThisAttempt > options.MaxTestRunsPerAttempt)
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

    [Description("Report that a test you wrote shows the production code does not behave as intended. Keep the test and its assertion, skip it with the reason 'suspected-bug: <title>', and never change production code or the assertion to make it pass. At most three per run.")]
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
        if (_bugs.Count >= SuspectedBug.MaxPerRun)
        {
            return new BugRecorded(false, _bugs.Count, "A run reports at most three suspected bugs; this one was not recorded.");
        }
        _bugs.Add(new SuspectedBug(file, test, Clip(title, 120), Clip(description, 2_000), Clip(expected, 500), Clip(actual, 500),
            Clip(failure, 2_000)));
        return new BugRecorded(true, _bugs.Count, null);
    }

    public static TestRun Summary(RunnerResult r) => new(r.Status, r.Build, r.Diagnostics.Take(30).ToList(), r.Tests,
        r.Failures.Take(20).ToList(), r.TargetPct, r.Uncovered,
        r.Status == RunnerStatus.DiffRejected ? "Your changes could not be applied to the commit." : null);

    private string Rel(string full) => Path.GetRelativePath(workspace.Root, full).Replace('\\', '/');

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..max];
}
