using Maf.Lab.Api.Agent;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Intent;
using Maf.Lab.Retrieval.Auth;
using Maf.Lab.Retrieval.Jev;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Api.Endpoints;

public static class JevStatsEndpoints
{
    public static IEndpointRouteBuilder MapJevStats(this IEndpointRouteBuilder app)
    {
        // Every Jev call site on this firm's turns — intent, guardrail, relevance and routing — over a window the
        // caller picks from a list. The firm is the caller's own; there is no way to name another. Numbers only: the
        // traces it reads stay on the server.
        app.MapGet("/api/admin/jev-stats", async (string? window, IPrincipalAccessor principals, IDbContextFactory<MafDbContext> db,
            IOptions<JevOptions> jev, IOptions<GuardOptions> guard, TimeProvider time, CancellationToken ct) =>
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
            var g = guard.Value;
            var crossTenant = g.PromptBlockAtByQuestion.TryGetValue("guard_cross_tenant", out var t) ? t : g.PromptBlockAt;
            return Results.Ok(JevStatistics.Aggregate(rows, chosen,
                new IntentStatsSettings(o.Model, o.MinConfidence, o.MinInDomain, o.TimeoutSeconds),
                new JevStatistics.GuardSettings(g.Enabled, g.PromptBlockAt, g.ContentWithholdAt, crossTenant), now));
        }).RequireAuthorization(AuthPolicies.FirmAdmin);

        return app;
    }
}
