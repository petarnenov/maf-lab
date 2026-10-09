using Maf.Lab.Domain.Admin;
using Maf.Lab.Plugins.Abstractions;
using Maf.Lab.TestGen;
using Maf.Lab.TestGen.Coverage;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Plugins.Coverage;

/// <summary>
/// Refresh coverage: both toolchains measured by the runner at the main branch's commit, ingested as official.
/// It runs as an admin job, so at most one refresh runs across replicas and a second request is answered with the
/// one in progress.
/// </summary>
public sealed class CoverageRefresher(
    IInstallationJobs jobs,
    CoverageRunnerClient runner,
    CoverageIngestor ingestor,
    IRepository repository,
    IOptions<CoverageOptions> options,
    ILogger<CoverageRefresher> logger)
{
    public const string Kind = "coverage.refresh";

    public Task<AdminJob> StartAsync(CancellationToken ct) => jobs.StartAsync(Kind, RefreshAsync, ct);

    public Task<AdminJob?> GetAsync(string jobId, CancellationToken ct) => jobs.GetAsync(jobId, ct);

    /// <summary>Stops a running refresh (stop-anything); the runner job it waits on is cancelled with it.</summary>
    public Task<AdminJobCancelResult> CancelAsync(string jobId, CancellationToken ct) => jobs.CancelAsync(jobId, ct);

    public Task<AdminJob?> CurrentAsync(CancellationToken ct) => jobs.CurrentAsync(ct);

    private async Task<string> RefreshAsync(CancellationToken ct)
    {
        var main = options.Value.MainBranch;
        var commit = await repository.ResolveAsync(main, ct)
            ?? throw new AdminJobFailure($"The {main} branch has no commit.");
        var parts = new List<string>();
        foreach (var toolchain in new[] { Toolchains.Dotnet, Toolchains.Vitest })
        {
            RunnerResult result;
            try
            {
                result = await runner.RunAsync(new RunnerRequest(commit, toolchain, Tests: TestScope.All), ct);
            }
            catch (RunnerUnavailableException ex)
            {
                throw new AdminJobFailure("The coverage runner could not be reached.", ex);
            }
            if (result.CoberturaXml is not { Length: > 0 } xml)
            {
                logger.LogWarning("coverage refresh {Toolchain} produced no report: {Status} build={Build}", toolchain, result.Status, result.Build);
                parts.Add($"{toolchain}: no report ({result.Status})");
                continue;
            }
            // Coverage of a run with failing tests is still what the tests covered; the summary says they failed.
            var ingested = await ingestor.IngestAsync(xml, commit, dirty: false, toolchain, SnapshotKind.Official, null, result.MeasuredRoot, ct);
            logger.LogInformation("coverage refresh {Toolchain}: {Files} files, {Dropped} dropped, {Failed} failing tests",
                toolchain, ingested.Files, ingested.Dropped, result.Tests.Failed);
            parts.Add($"{toolchain}: {ingested.Files} files" + (result.Tests.Failed > 0 ? $", {result.Tests.Failed} failing tests" : ""));
        }
        if (parts.All(p => p.Contains("no report", StringComparison.Ordinal)))
        {
            throw new AdminJobFailure("Neither toolchain produced a coverage report.");
        }
        return $"{commit[..7]} — " + string.Join("; ", parts);
    }
}
