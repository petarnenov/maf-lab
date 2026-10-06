using System.Text.Json;
using Maf.Lab.Domain.SharedState;
using Maf.Lab.Domain.Tracing;
using Maf.Lab.Plugins.Abstractions;
using Maf.Lab.Retrieval.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Maf.Lab.Plugins.Monitor;

/// <summary>A run's trace so far, and whether the run is over (then the turn's stored trace is the whole of it).</summary>
public sealed record LiveTraceDocument(string RunId, string? TurnId, bool Ended, IReadOnlyList<TraceEvent> Events);

/// <summary>The monitor's two reads of a trace: a run's while it is written, and a turn's once kept.</summary>
public static class TraceEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        // A run's trace while it is being written (agui-protocol-only): the trace never travels on the run's stream, so
        // the monitor reads it here while the run is live, from whichever replica it reaches. Only the run's owner.
        app.MapGet("/api/runs/{runId}/trace", async (string runId, int? after, IPrincipalAccessor principals, IRunStateStore runs,
            IRunTraceStore traces, CancellationToken ct) =>
        {
            var principal = principals.Current;
            var state = await runs.GetAsync(runId, ct);
            if (state is null || state.UserId != principal.UserId || state.TenantId != principal.TenantId.Value)
            {
                return Results.NotFound();
            }
            var events = await traces.ReadAsync(runId, Math.Max(0, after ?? 0), ct);
            return Results.Ok(new LiveTraceDocument(runId, state.TurnId, state.Outcome != RunOutcomes.Running, events));
        }).RequireAuthorization();

        // Owner, or a TENANT_ADMIN of the same tenant for turns in the review queue (turns with signals). Everyone else: 404,
        // and so is a turn whose trace was never kept or has passed the monitor's retention.
        app.MapGet("/api/turns/{turnId}/trace", async (string turnId, IPrincipalAccessor principals, ITurnAccess access,
            IDbContextFactory<DbContext> db, CancellationToken ct) =>
        {
            var principal = principals.Current;
            await using var ctx = await db.CreateDbContextAsync(ct);
            var trace = await ctx.Set<TurnDiagnosticsRow>().AsNoTracking()
                .FirstOrDefaultAsync(t => t.TurnId == turnId && t.TenantId == principal.TenantId.Value, ct);
            // Who may read a turn is the core's rule (its owner, or an admin while it is under review), asked of the core.
            if (trace is null || !await access.MayReadAsync(turnId, ct))
            {
                return Results.NotFound();
            }
            var events = JsonSerializer.Deserialize<List<TraceEvent>>(trace.Json, MonitorObserver.Json) ?? [];
            // Null, not empty: a turn whose frames were never kept has none to show, and says so.
            var frames = trace.AguiJson is null ? null : JsonSerializer.Deserialize<List<RunFrame>>(trace.AguiJson, MonitorObserver.Json);
            return Results.Ok(new TurnTraceDocument(trace.TurnId, trace.ConversationId,
                new DateTimeOffset(DateTime.SpecifyKind(trace.CreatedAt, DateTimeKind.Utc)), events, frames));
        }).RequireAuthorization();
    }
}
