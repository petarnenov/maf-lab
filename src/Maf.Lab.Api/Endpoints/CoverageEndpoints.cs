using Maf.Lab.Api.Coverage;
using Maf.Lab.Api.Storage;
using Maf.Lab.TestGen;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Api.Endpoints;

/// <summary>
/// The Coverage screen's API. Reading is for anyone signed in; everything that changes state is for a firm admin.
/// Coverage describes the repository, so nothing here is scoped to a firm.
/// </summary>
public static class CoverageEndpoints
{
    public sealed record LineDto(int Line, int Hits, int BranchesCovered, int BranchesTotal, string Status);

    public sealed record FileSummaryDto(int LinesTotal, int LinesCovered, int BranchesTotal, int BranchesCovered, double Pct,
        int Threshold, bool ThresholdIsOverride);

    /// <summary>A file as of one snapshot: its source at that commit and the status of each executable line.</summary>
    public sealed record FileDetailDto(string Path, string? Toolchain, string Commit, DateTime MeasuredAt, bool Dirty, string Kind,
        FileSummaryDto Summary, string Source, IReadOnlyList<LineDto> Lines, RunSummary? Run);

    public sealed record HistoryEntryDto(string SnapshotId, string Commit, DateTime MeasuredAt, double Pct, int LinesCovered, int LinesTotal, string Kind);

    public sealed record IngestResponse(string SnapshotId, int Files, int Dropped);

    public static IEndpointRouteBuilder MapCoverage(this IEndpointRouteBuilder app)
    {
        var read = app.MapGroup("/api/coverage").RequireAuthorization();
        var admin = app.MapGroup("/api/coverage").RequireAuthorization(AuthPolicies.FirmAdmin);

        read.MapGet("/tree", async (CoverageStore store, IDbContextFactory<MafDbContext> db, IOptions<CoverageOptions> options,
            CancellationToken ct) => Results.Ok(await TreeAsync(store, db, options.Value, ct)));

        read.MapGet("/files", async (string? path, string? run, CoverageStore store, IRepository repository,
            IDbContextFactory<MafDbContext> db, IOptions<CoverageOptions> options, CancellationToken ct) =>
        {
            // Only paths some snapshot has: the source of anything else is never read.
            if (CoveragePaths.Clean(path ?? "") is not { } clean || clean != path || !await store.KnowsAsync(clean, ct))
            {
                return NotFound();
            }
            var snapshot = run is { Length: > 0 }
                ? await store.CandidateAsync(run, clean, ct)
                : await store.CurrentAsync(clean, ct);
            if (snapshot is null)
            {
                return NotFound();
            }
            var source = await repository.ShowAsync(snapshot.Totals.CommitSha, clean, ct);
            if (source is null)
            {
                return NotFound();
            }
            await using var context = await db.CreateDbContextAsync(ct);
            var overrides = await OverridesAsync(context, ct);
            var threshold = CoverageTree.EffectiveThreshold(clean, overrides, options.Value.DefaultThresholdPct);
            var runs = await context.TestGenRuns.AsNoTracking().Where(r => r.Path == clean)
                .OrderByDescending(r => r.UpdatedAt).Take(10).ToListAsync(ct);
            var current = runs.FirstOrDefault(r => TestGenRunState.Active.Contains(r.State)) ?? runs.FirstOrDefault();
            var t = snapshot.Totals;
            return Results.Ok(new FileDetailDto(
                clean, Toolchains.For(clean), t.CommitSha, t.MeasuredAt, snapshot.Dirty, snapshot.Kind,
                new FileSummaryDto(t.LinesTotal, t.LinesCovered, t.BranchesTotal, t.BranchesCovered, t.LinePct, threshold, overrides.ContainsKey(clean)),
                source,
                snapshot.Lines.Select(l => new LineDto(l.Line, l.Hits, l.BranchesCovered, l.BranchesTotal, LineStatus.Of(l))).ToList(),
                current is null ? null : RunSummary.Of(current)));
        });

        read.MapGet("/files/history", async (string? path, CoverageStore store, CancellationToken ct) =>
        {
            if (CoveragePaths.Clean(path ?? "") is not { } clean || clean != path)
            {
                return NotFound();
            }
            var history = await store.HistoryAsync(clean, ct);
            return history.Count == 0
                ? NotFound()
                : Results.Ok(history.Select(h => new HistoryEntryDto(h.Totals.SnapshotId, h.Totals.CommitSha, h.Totals.MeasuredAt,
                    h.Totals.LinePct, h.Totals.LinesCovered, h.Totals.LinesTotal, h.Kind)).ToList());
        });

        admin.MapPost("/reports", async (HttpRequest request, CoverageIngestor ingestor, IRepository repository,
            IOptions<CoverageOptions> options, CancellationToken ct) =>
        {
            if (!request.HasFormContentType)
            {
                return Invalid("report", "Send the report as multipart/form-data with commit, toolchain and report.");
            }
            var form = await request.ReadFormAsync(ct);
            var toolchain = form["toolchain"].ToString();
            if (!Toolchains.IsKnown(toolchain))
            {
                return Invalid("toolchain", "toolchain must be dotnet or vitest.");
            }
            var commit = await repository.ResolveAsync(form["commit"].ToString(), ct);
            if (commit is null)
            {
                return Invalid("commit", "commit must name a commit of this repository.");
            }
            if (form.Files.GetFile("report") is not { Length: > 0 } file)
            {
                return Invalid("report", "report must be a Cobertura XML file.");
            }
            if (file.Length > options.Value.MaxReportBytes)
            {
                return Invalid("report", "The report is larger than the upload limit.");
            }
            try
            {
                await using var stream = file.OpenReadStream();
                var dirty = bool.TryParse(form["dirty"].ToString(), out var d) && d;
                var measuredRoot = form["root"].ToString() is { Length: > 0 } r ? r : null;
                var result = await ingestor.IngestAsync(stream, commit, dirty, toolchain, SnapshotKind.Official, null, measuredRoot, ct);
                return Results.Ok(new IngestResponse(result.SnapshotId, result.Files, result.Dropped));
            }
            catch (CoberturaFormatException ex)
            {
                return Invalid("report", ex.Message);
            }
        }).DisableAntiforgery();

        admin.MapPost("/refresh", async (CoverageRefresher refresher, CancellationToken ct) =>
        {
            var job = await refresher.StartAsync(ct);
            return Results.Accepted($"/api/coverage/refresh/{job.JobId}", job);
        });

        read.MapGet("/refresh", async (CoverageRefresher refresher, CancellationToken ct) =>
            await refresher.CurrentAsync(ct) is { } job ? Results.Ok(job) : Results.NoContent());

        read.MapGet("/refresh/{jobId}", async (string jobId, CoverageRefresher refresher, CancellationToken ct) =>
            await refresher.GetAsync(jobId, ct) is { } job ? Results.Ok(job) : NotFound());

        return app;
    }

    public static async Task<CoverageTreeDto> TreeAsync(CoverageStore store, IDbContextFactory<MafDbContext> db, CoverageOptions options,
        CancellationToken ct)
    {
        var current = await store.LatestOfficialAsync(ct);
        await using var context = await db.CreateDbContextAsync(ct);
        var overrides = await OverridesAsync(context, ct);
        // Every active run, and each file's latest finished one, so an outcome stays visible after the run ends.
        var rows = await context.TestGenRuns.AsNoTracking().ToListAsync(ct);
        var runs = rows.GroupBy(r => r.Path)
            .SelectMany(g => g.Where(r => TestGenRunState.Active.Contains(r.State))
                .DefaultIfEmpty(g.OrderByDescending(r => r.UpdatedAt).First()))
            .Select(RunSummary.Of)
            .ToList();
        var candidates = await store.CandidatesAsync(runs.Where(r => r.State == TestGenRunState.Candidate).Select(r => r.Id).ToList(), ct);
        return CoverageTree.Build(current, overrides, options.DefaultThresholdPct, candidates, runs);
    }

    internal static async Task<IReadOnlyDictionary<string, int>> OverridesAsync(MafDbContext context, CancellationToken ct) =>
        await context.CoverageThresholds.AsNoTracking().ToDictionaryAsync(t => t.Path, t => t.Pct, ct);

    internal static IResult NotFound() =>
        Results.Problem(type: "not_found", title: "Not found", detail: "No coverage for that file.", statusCode: StatusCodes.Status404NotFound);

    internal static IResult Invalid(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}
