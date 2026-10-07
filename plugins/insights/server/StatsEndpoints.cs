using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Plugins.Abstractions;
using Maf.Lab.Retrieval.Jev;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Plugins.Insights;

/// <summary>
/// The statistics routes, over a window the caller picks from a list. Both read the turns' core records (introduce-plugins
/// 5.3) through the core's <see cref="ITurnRecords"/>, which exist with or without the monitor; the firm is the caller's
/// own, and there is no way to name another. Numbers only: the records they read stay on the server.
/// </summary>
public static class StatsEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        // How the intent classifier behaved on this firm's turns.
        app.MapGet("/api/admin/intent-stats", async (string? window, ITurnRecords turns, IOptions<JevOptions> jev,
            TimeProvider time, CancellationToken ct) =>
        {
            var chosen = window ?? "24h";
            if (!IntentStatistics.Windows.TryGetValue(chosen, out var w))
            {
                return InvalidWindow();
            }
            var now = time.GetUtcNow();
            var rows = await ReadAsync(turns, now - w.Span, ct);
            return Results.Ok(IntentStatistics.Aggregate(rows, chosen, Settings(jev.Value), now));
        }).RequireAuthorization(PolicyNames.TenantAdmin);

        // Every Jev call site on this firm's chat turns — intent, guardrail, relevance and routing. The A2A path has no
        // turn trace and is not counted.
        app.MapGet("/api/admin/jev-stats", async (string? window, ITurnRecords turns, IOptions<JevOptions> jev,
            IGuardSettings guard, TimeProvider time, CancellationToken ct) =>
        {
            var chosen = window ?? "24h";
            if (!IntentStatistics.Windows.TryGetValue(chosen, out var w))
            {
                return InvalidWindow();
            }
            var now = time.GetUtcNow();
            var rows = await ReadAsync(turns, now - w.Span, ct);
            return Results.Ok(JevStatistics.Aggregate(rows, chosen, Settings(jev.Value), guard.Current, now));
        }).RequireAuthorization(PolicyNames.TenantAdmin);
    }

    private static async Task<List<IntentStatistics.TraceRow>> ReadAsync(ITurnRecords turns, DateTimeOffset from, CancellationToken ct) =>
        (await turns.SinceAsync(from, ct)).Select(t => new IntentStatistics.TraceRow(t.CreatedAt, t.RecordJson)).ToList();

    private static IntentStatsSettings Settings(JevOptions o) => new(o.Model, o.MinConfidence, o.MinInDomain, o.TimeoutSeconds);

    private static IResult InvalidWindow() => Results.ValidationProblem(new Dictionary<string, string[]>
    {
        ["window"] = [$"window must be one of: {string.Join(", ", IntentStatistics.Windows.Keys)}."],
    });
}
