using System.Text.Json;
using System.Text.RegularExpressions;
using Maf.Lab.Api.Storage;
using Maf.Lab.TestGen;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Api.Coverage;

/// <summary>
/// What the api does with a completed task before anyone sees its result: it believes nothing it was told. The diff's
/// paths and the guardrails are checked again; every suspected bug is proved by running its test un-skipped; the tests
/// are run in a fresh workspace and must all pass; only then are issues opened, the candidate recorded, and the branch
/// made. Any step failing ends the run <c>verification_failed</c> with the reason, and coverage stays as it was.
/// </summary>
public sealed partial class RunVerifier(
    IDbContextFactory<MafDbContext> dbFactory,
    TestGenRuns runs,
    RepoWriter repo,
    CoverageRunnerClient runner,
    CoverageIngestor ingestor,
    GitHubIssues github,
    TimeProvider time,
    IOptions<CoverageRunnerOptions> runnerOptions,
    ILogger<RunVerifier> logger) : IRunVerifier
{
    public const string NotReproduced = "suspected bug not reproduced";
    public const string NotLintClean = "the tests do not pass lint (warnings, ESLint or Prettier in the diff's files)";

    public async Task VerifyAsync(string runId, CancellationToken ct)
    {
        var run = await runs.GetAsync(runId, ct);
        if (run is not { State: TestGenRunState.Verifying } || run.ReportJson is null)
        {
            return;
        }
        var report = JsonSerializer.Deserialize<TestGenReport>(run.ReportJson, TestGenKinds.Json)!;
        var bugs = report.SuspectedBugs ?? [];
        if (report.Diff.Length == 0)
        {
            await runs.FinishAsync(runId, TestGenRunState.CompletedNoChange, report.StopReason, ct);
            return;
        }

        var forbidden = DiffPaths.Forbidden(report.Diff, run.Toolchain);
        if (forbidden.Count > 0)
        {
            await FailAsync(runId, "the diff changes files outside the test locations", ct);
            return;
        }

        await using var copy = await repo.CheckOutAsync(run.CommitSha, report.Diff, ct);
        if (copy is null)
        {
            await FailAsync(runId, "the diff does not apply to its commit", ct);
            return;
        }
        var files = await copy.ChangedFilesAsync(ct);
        if (TestGuardrails.Check(files, bugs, run.MaxSuspectedBugs ?? SuspectedBug.MaxPerRun) is { Count: > 0 } violations)
        {
            await FailAsync(runId, $"guardrail: {violations[0]}", ct);
            return;
        }

        // Each suspected bug: its test, un-skipped, must fail against the unchanged code.
        foreach (var bug in bugs)
        {
            if (!files.TryGetValue(bug.TestFile, out var content) || Unskip(content, bug) is not { } unskipped)
            {
                await FailAsync(runId, $"suspected bug {bug.Test} has no skipped test to check", ct);
                return;
            }
            await copy.WriteAsync(bug.TestFile, unskipped, ct);
            // Only the related tests: the un-skipped test is in a file the diff changes, so it is among them.
            var proof = await runner.RunAsync(new RunnerRequest(run.CommitSha, run.Toolchain, await copy.DiffAsync(ct), run.Path, TestScope.Related), ct);
            await copy.WriteAsync(bug.TestFile, content, ct);
            // The proof's copy is never merged: what counts is whether its tests ran, not whether it lints.
            if (!proof.Measured && !LintDiagnostics.OnlyLint(proof))
            {
                await FailAsync(runId, $"suspected bug {bug.Test} could not be checked ({proof.Status}, build {proof.Build})", ct);
                return;
            }
            if (!proof.Failures.Any(f => f.Name.Contains(bug.Test, StringComparison.Ordinal)))
            {
                await FailAsync(runId, NotReproduced, ct);
                return;
            }
        }

        // The run itself, measured by the api, not reported by the agent, on the whole suite: a test the diff breaks
        // anywhere, or one that fails only beside the others, fails it here. The runner may answer with the result it
        // computed for the agent's confirmation of this same diff: still the runner's own measurement, never the agent's.
        var measured = await runner.RunAsync(new RunnerRequest(run.CommitSha, run.Toolchain, report.Diff, run.Path, TestScope.All,
            Fresh: !runnerOptions.Value.ReuseForVerification), ct);
        if (!measured.Measured)
        {
            await FailAsync(runId, measured.Status != RunnerStatus.Ok ? $"the runner reported {measured.Status}"
                : LintDiagnostics.OnlyLint(measured) ? NotLintClean : "the tests do not build", ct);
            return;
        }
        if (measured.Tests.Failed > 0)
        {
            await FailAsync(runId, $"{measured.Tests.Failed} test(s) fail", ct);
            return;
        }

        // Confirmed bugs become issues, and each skip points at its issue.
        foreach (var bug in bugs)
        {
            var link = await IssueForAsync(run, bug, ct);
            var marker = link is null ? $"{SuspectedBug.Marker} (no issue: GitHub not configured)" : $"{SuspectedBug.Marker} {link}";
            var text = files[bug.TestFile].Replace(bug.SkipReason, $"{marker}: {bug.Title}", StringComparison.Ordinal);
            files = new Dictionary<string, string>(files) { [bug.TestFile] = text };
            await copy.WriteAsync(bug.TestFile, text, ct);
        }
        var finalDiff = await copy.DiffAsync(ct);

        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            // Verification repeated after a restart records the candidate once.
            if (!await db.CoverageSnapshots.AnyAsync(s => s.RunId == runId, ct) && measured.CoberturaXml is { } xml)
            {
                await ingestor.IngestAsync(xml, run.CommitSha, dirty: false, run.Toolchain, SnapshotKind.Candidate, runId, measured.MeasuredRoot, ct);
            }
        }
        var branch = RepoWriter.BranchFor(run.Path, runId);
        await repo.CommitBranchAsync(copy, branch,
            $"Add tests for {run.Path} ({Pct(report.Baseline)} → {Pct(measured.TargetPct)})\n\nTest-agent run {runId}, model {run.Model}.", ct);
        await runs.FinishAsync(runId, TestGenRunState.Candidate, report.StopReason, ct, r =>
        {
            r.Branch = branch;
            r.LastPct = measured.TargetPct;
            r.ReportJson = JsonSerializer.Serialize(report with
            {
                Diff = finalDiff,
                Final = measured.TargetPct,
                Verification = new VerificationRun(measured.Selection?.Scope ?? TestScope.All, measured.Tests, measured.TargetPct, measured.ReusedFrom),
            }, TestGenKinds.Json);
        });
        logger.LogInformation("run {RunId} verified: {Pct}% on {Branch}, {Bugs} suspected bug(s), reused={Reused} runner job {JobId}",
            runId, measured.TargetPct, branch, bugs.Count, measured.ReusedFrom is not null, measured.ReusedFrom?.JobId);
    }

    /// <summary>
    /// The issue for one confirmed bug: found in the table, or (after a restart mid-call) on GitHub by its marker, or
    /// opened now. Null when GitHub is not configured.
    /// </summary>
    private async Task<string?> IssueForAsync(TestGenRunRow run, SuspectedBug bug, CancellationToken ct)
    {
        if (!github.Configured)
        {
            return null;
        }
        var key = $"{bug.TestFile}::{bug.Test}";
        var marker = $"<!-- maf-lab:testgen run={run.Id} test={key} -->";
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.TestGenIssues.SingleOrDefaultAsync(i => i.RunId == run.Id && i.TestKey == key, ct);
        if (row is { State: "created", Url: { } done })
        {
            return done;
        }
        if (row is null)
        {
            row = new TestGenIssueRow { RunId = run.Id, TestKey = key, State = "creating", Title = bug.Title, CreatedAt = time.GetUtcNow().UtcDateTime };
            db.TestGenIssues.Add(row);
            await db.SaveChangesAsync(ct);
        }
        else if (await github.FindAsync(marker, ct) is { } found)
        {
            // A previous attempt opened it and stopped before saying so.
            (row.State, row.Number, row.Url) = ("created", found.Number, found.Url);
            await db.SaveChangesAsync(ct);
            return found.Url;
        }
        var opened = await github.CreateAsync($"Suspected bug: {bug.Title} ({run.Path})", IssueBody(run, bug, marker), ct);
        (row.State, row.Number, row.Url) = ("created", opened.Number, opened.Url);
        await db.SaveChangesAsync(ct);
        return opened.Url;
    }

    internal static string IssueBody(TestGenRunRow run, SuspectedBug bug, string marker) => $"""
        The test agent wrote a test for `{run.Path}` that fails because the code does not do what it is meant to. The
        test was kept, with its assertion, and skipped; the production code was not changed. The api ran the test
        un-skipped against commit `{run.CommitSha[..12]}` and it failed.

        **What is wrong:** {bug.Description}

        **Expected:** {bug.Expected}

        **Actual:** {bug.Actual}

        **Failing test:** `{bug.Test}` in `{bug.TestFile}`

        ```
        {bug.Failure}
        ```

        Run `{run.Id}` · model `{run.Model}` · branch `{RepoWriter.BranchFor(run.Path, run.Id)}`. When the code is fixed,
        remove the skip and the test should pass.

        {marker}
        """;

    /// <summary>The test file with this bug's test no longer skipped, or null when the skip is not found.</summary>
    internal static string? Unskip(string content, SuspectedBug bug)
    {
        var name = Regex.Escape(bug.Test.Split('.').Last());
        var cs = new Regex($@"(\[(?:Fact|Theory))\s*\(\s*Skip\s*=\s*""{Regex.Escape(SuspectedBug.Marker)}[^""]*""\s*\)(\][^\[]*?\bvoid\s+{name}\s*\()");
        if (cs.IsMatch(content))
        {
            return cs.Replace(content, "$1$2", 1);
        }
        var ts = new Regex($@"(\b(?:it|test))\s*\.\s*skip(\s*\(\s*['""`]{Regex.Escape(bug.Test)}['""`])");
        return ts.IsMatch(content) ? ts.Replace(content, "$1$2", 1) : null;
    }

    private Task FailAsync(string runId, string reason, CancellationToken ct)
    {
        logger.LogInformation("run {RunId} failed verification: {Reason}", runId, reason);
        return runs.FinishAsync(runId, TestGenRunState.VerificationFailed, reason, ct);
    }

    private static string Pct(double? pct) => pct is { } p ? $"{p:0.0}%" : "?";
}
