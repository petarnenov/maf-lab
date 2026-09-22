using System.Text.Json;
using Maf.Lab.Api.Agent.Tracing;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Tracing;
using Maf.Lab.Retrieval.Auth;
using Microsoft.EntityFrameworkCore;

namespace Maf.Lab.Api.Endpoints;

public static class TraceEndpoints
{
    public static IEndpointRouteBuilder MapTraces(this IEndpointRouteBuilder app)
    {
        // Owner, or a FIRM_ADMIN of the same firm for turns in the review queue (turns with signals). Everyone else: 404.
        app.MapGet("/api/turns/{turnId}/trace", async (string turnId, IPrincipalAccessor principals, IDbContextFactory<MafDbContext> db, CancellationToken ct) =>
        {
            var principal = principals.Current;
            await using var ctx = await db.CreateDbContextAsync(ct);
            var trace = await ctx.TurnTraces.AsNoTracking().FirstOrDefaultAsync(t => t.TurnId == turnId && t.FirmId == principal.FirmId.Value, ct);
            if (trace is null)
            {
                return Results.NotFound();
            }
            var allowed = trace.UserId == principal.UserId
                || principal.IsFirmAdmin && await ctx.Turns.AnyAsync(t => t.Id == turnId && t.FirmId == principal.FirmId.Value && t.SignalsJson != "[]", ct);
            if (!allowed)
            {
                return Results.NotFound();
            }
            var events = JsonSerializer.Deserialize<List<TraceEvent>>(trace.Json, TurnTrace.Json) ?? [];
            // Null, not empty: a turn answered before the frames were kept has none to show, and says so.
            var frames = trace.AguiJson is null
                ? null
                : JsonSerializer.Deserialize<List<RunFrame>>(trace.AguiJson, TurnTrace.Json);
            return Results.Ok(new TurnTraceDocument(trace.TurnId, trace.ConversationId,
                new DateTimeOffset(DateTime.SpecifyKind(trace.CreatedAt, DateTimeKind.Utc)), events, frames));
        }).RequireAuthorization();
        return app;
    }
}
