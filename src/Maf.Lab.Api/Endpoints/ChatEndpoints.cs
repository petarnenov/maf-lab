using System.Text.Json;
using Maf.Lab.Api.Agent;
using Maf.Lab.Domain.Chat;
using Maf.Lab.Domain.SharedState;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Retrieval.Auth;

namespace Maf.Lab.Api.Endpoints;

public static class ChatEndpoints
{
    public static IEndpointRouteBuilder MapChat(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();

        api.MapGet("/me", (IPrincipalAccessor principals) =>
        {
            var p = principals.Current;
            return Results.Ok(new { p.UserId, FirmId = p.FirmId.Value, Role = p.Role.ToString(), p.AllowedAdvisorIds });
        });

        api.MapPost("/conversations", async (IPrincipalAccessor principals, ConversationService conversations, CancellationToken ct) =>
            Results.Created((string?)null, new ConversationCreated(await conversations.CreateAsync(principals.Current, ct))));

        // A run's trace while it is being written (agui-protocol-only): the trace no longer travels on the run's stream,
        // so the monitor reads it here while the run is live, from whichever replica it reaches. Only the run's owner.
        api.MapGet("/runs/{runId}/trace", async (string runId, int? after, IPrincipalAccessor principals, IRunStateStore runs,
            IRunTraceStore traces, CancellationToken ct) =>
        {
            var principal = principals.Current;
            var state = await runs.GetAsync(runId, ct);
            if (state is null || state.UserId != principal.UserId || state.FirmId != principal.FirmId.Value)
            {
                return Results.NotFound();
            }
            var events = await traces.ReadAsync(runId, Math.Max(0, after ?? 0), ct);
            return Results.Ok(new LiveTraceDocument(runId, state.TurnId, state.Outcome != RunOutcomes.Running, events));
        });

        return app;
    }
}

/// <summary>A run's trace so far, and whether the run is over (then the turn's stored trace is the whole of it).</summary>
public sealed record LiveTraceDocument(string RunId, string? TurnId, bool Ended, IReadOnlyList<Maf.Lab.Domain.Tracing.TraceEvent> Events);
