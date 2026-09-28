using Maf.Lab.Api.Agent;
using Maf.Lab.Api.Agent.Jev;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Intent;
using Maf.Lab.Retrieval.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Api.Endpoints;

public static class IntentStatsEndpoints
{
    public static IEndpointRouteBuilder MapIntentStats(this IEndpointRouteBuilder app)
    {
        // How the intent classifier behaved on this firm's turns, over a window the caller picks from a list. The firm is
        // the caller's own; there is no way to name another. Numbers only: the traces it reads stay on the server.
        app.MapGet("/api/admin/intent-stats", async (string? window, IPrincipalAccessor principals, IDbContextFactory<MafDbContext> db,
            IOptions<JevOptions> jev, TimeProvider time, CancellationToken ct) =>
        {
            var chosen = window ?? "24h";
            if (!IntentStatistics.Windows.TryGetValue(chosen, out var w))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["window"] = [$"window must be one of: {string.Join(", ", IntentStatistics.Windows.Keys)}."],
                });
            }
            var principal = principals.Current;
            var now = time.GetUtcNow();
            var from = (now - w.Span).UtcDateTime;
            await using var ctx = await db.CreateDbContextAsync(ct);
            var rows = await ctx.TurnTraces.AsNoTracking()
                .Where(t => t.FirmId == principal.FirmId.Value && t.CreatedAt >= from)
                .Select(t => new IntentStatistics.TraceRow(t.CreatedAt, t.Json))
                .ToListAsync(ct);
            var o = jev.Value;
            return Results.Ok(IntentStatistics.Aggregate(rows, chosen,
                new IntentStatsSettings(o.Model, o.MinConfidence, o.MinInDomain, o.TimeoutSeconds), now));
        }).RequireAuthorization(AuthPolicies.FirmAdmin);

        return app;
    }
}
