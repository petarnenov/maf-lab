using System.Text.Json;
using Maf.Lab.Api.Feedback;
using Maf.Lab.Domain.Evals;

namespace Maf.Lab.Api.Endpoints;

/// <summary>Read-only view of eval reports written by Maf.Lab.Eval under {Evals:Root}/reports.</summary>
public static class EvalReportEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapEvalReports(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/evals").RequireAuthorization();

        api.MapGet("/reports", async (DatasetWriter datasets, CancellationToken ct) =>
        {
            var reports = new List<EvalReportSummary>();
            foreach (var report in await LoadAllAsync(datasets.Root, ct))
            {
                reports.Add(new EvalReportSummary(report.RunId, report.Suite, report.StartedAt, report.Passed, report.Variants));
            }
            return Results.Ok(reports.OrderByDescending(r => r.StartedAt).ToList());
        });

        api.MapGet("/reports/{runId}", async (string runId, DatasetWriter datasets, CancellationToken ct) =>
        {
            var report = (await LoadAllAsync(datasets.Root, ct)).FirstOrDefault(r => r.RunId == runId);
            return report is null ? Results.NotFound() : Results.Ok(report);
        });

        return app;
    }

    private static async Task<List<EvalReport>> LoadAllAsync(string root, CancellationToken ct)
    {
        var dir = Path.Combine(root, "reports");
        var reports = new List<EvalReport>();
        if (!Directory.Exists(dir))
        {
            return reports;
        }
        foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
        {
            try
            {
                await using var stream = File.OpenRead(file);
                if (await JsonSerializer.DeserializeAsync<EvalReport>(stream, Json, ct) is { } report)
                {
                    reports.Add(report);
                }
            }
            catch (JsonException)
            {
                // Skip partially written or foreign files.
            }
        }
        return reports;
    }
}
