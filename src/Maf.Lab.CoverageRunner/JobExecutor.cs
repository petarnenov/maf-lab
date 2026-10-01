using System.Diagnostics;
using System.Text;
using Maf.Lab.TestGen;
using Microsoft.Extensions.Options;

namespace Maf.Lab.CoverageRunner;

/// <summary>
/// One job, start to end: a fresh workspace at the commit, the diff applied (or refused), the toolchain run with its
/// report written to a folder outside the workspace, the target file's coverage read, and the workspace deleted.
/// </summary>
public sealed class JobExecutor(IOptions<RunnerOptions> options, IEnumerable<IToolchainRunner> toolchains, IHostEnvironment environment,
    ILogger<JobExecutor> logger)
{
    public async Task<RunnerResult> RunAsync(RunnerRequest request, CancellationToken ct)
    {
        var opts = options.Value;
        var clock = Stopwatch.StartNew();
        var workRoot = string.IsNullOrWhiteSpace(opts.WorkRoot) ? Path.Combine(Path.GetTempPath(), "maf-runner") : opts.WorkRoot;
        var job = Path.Combine(workRoot, $"job-{Guid.NewGuid():N}");
        var workspace = Path.Combine(job, "ws");
        // The report goes to a folder the tests are never told about, and is read only after they have exited.
        var output = Path.Combine(job, $"out-{Guid.NewGuid():N}");
        Directory.CreateDirectory(output);
        try
        {
            var repo = await RepoRootAsync(opts, ct);
            try
            {
                await Git.CheckedAsync(job, ["clone", "-q", "--shared", "--no-checkout", repo, workspace], ct);
                await Git.CheckedAsync(workspace, ["checkout", "-q", "--detach", request.Commit], ct);
            }
            catch (GitException ex)
            {
                logger.LogWarning("runner checkout failed git={Subcommand}", ex.Subcommand);
                return RunnerResult.Failed(RunnerStatus.CheckoutFailed, clock.ElapsedMilliseconds);
            }

            if (request.Diff is { Length: > 0 } diff)
            {
                var bytes = Encoding.UTF8.GetBytes(diff);
                var check = await Git.RunAsync(workspace, ["apply", "--check", "--whitespace=nowarn", "-"], ct, stdin: bytes);
                if (!check.Ok)
                {
                    return RunnerResult.Failed(RunnerStatus.DiffRejected, clock.ElapsedMilliseconds,
                        [check.Stderr.Replace(workspace + "/", "", StringComparison.Ordinal).Trim()]);
                }
                await Git.CheckedAsync(workspace, ["apply", "--whitespace=nowarn", "-"], ct, stdin: bytes);
            }

            var toolchain = toolchains.Single(t => t.Toolchain == request.Toolchain);
            var plan = request.Tests == TestScope.Related && request.TargetFile is { } focus
                ? RelatedTests.Plan(request.Toolchain, workspace, opts, focus, DiffPaths.Of(request.Diff ?? ""))
                : TestPlan.Whole;
            var outcome = await toolchain.RunAsync(workspace, output, plan, opts.TimeLimit, ct);
            if (plan.Related && outcome.RanNoTest)
            {
                // The related tests turned out to hold no test: the whole suite, in what is left of the time limit.
                plan = TestPlan.FellBack(RelatedTests.NothingSelected);
                var left = opts.TimeLimit - clock.Elapsed;
                outcome = left > TimeSpan.Zero
                    ? await toolchain.RunAsync(workspace, output, plan, left, ct)
                    : outcome with { TimedOut = true };
            }
            if (outcome.TimedOut)
            {
                return RunnerResult.Failed(RunnerStatus.TimedOut, clock.ElapsedMilliseconds);
            }

            string? xml = null;
            double? pct = null;
            IReadOnlyList<int[]> uncovered = [];
            LineHits? lines = null;
            if (outcome.CoberturaPath is { } report)
            {
                xml = await File.ReadAllTextAsync(report, ct);
                if (request.TargetFile is { } target)
                {
                    var files = (await Git.CheckedAsync(workspace, ["ls-files", "-z"], ct)).Text
                        .Split('\0', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
                    (pct, uncovered, lines) = TargetCoverage.Of(report, workspace, files, target);
                    // Measured, and not in the report at all: not one of its lines ran.
                    pct ??= 0;
                }
            }
            var selection = plan.Related && outcome.TestFiles is { Count: > 0 } ran
                ? plan.Selection with { TestFiles = ran.Take(TestSelection.MaxFiles).ToList() }
                : plan.Selection;
            logger.LogInformation("runner job {Toolchain} scope={Scope} files={Files} build={Build} passed={Passed} failed={Failed} ms={Ms}",
                request.Toolchain, selection.Scope, selection.TestFiles.Count, outcome.Build, outcome.Tests.Passed, outcome.Tests.Failed,
                clock.ElapsedMilliseconds);
            return new RunnerResult(RunnerStatus.Ok, outcome.Build, outcome.Diagnostics, outcome.Tests, outcome.Failures, xml,
                workspace, pct, uncovered, clock.ElapsedMilliseconds, selection, lines);
        }
        finally
        {
            try
            {
                Directory.Delete(job, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning("runner could not delete a workspace ({ErrorType})", ex.GetType().Name);
            }
        }
    }

    private async Task<string> RepoRootAsync(RunnerOptions opts, CancellationToken ct) =>
        !string.IsNullOrWhiteSpace(opts.RepoRoot)
            ? opts.RepoRoot
            : (await Git.CheckedAsync(environment.ContentRootPath, ["rev-parse", "--show-toplevel"], ct)).Text.Trim();
}
