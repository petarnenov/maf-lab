using System.Text.Json;
using Maf.Lab.Api.Compliance;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Retrieval.Auth;
using Microsoft.EntityFrameworkCore;

namespace Maf.Lab.Api.Endpoints;

/// <summary>What the agents have been doing, for the firm whose data they were doing it with.</summary>
public static class A2AAdminEndpoints
{
    /// <summary>An inbound task, as an operator needs to see it: no message content, only what happened.</summary>
    public sealed record InboundTask(string TaskId, string PartnerId, string Operation, string State,
        DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, long DurationMs, bool Cancellable);

    public sealed record OutboundConsultation(string TaskId, string Agent, string Outcome, long DurationMs, DateTimeOffset At);

    public sealed record PushDelivery(string TaskId, string State, string Url, int Attempts, bool Delivered,
        string? Error, DateTimeOffset At);

    public sealed record A2AActivity(IReadOnlyList<InboundTask> Inbound, IReadOnlyList<OutboundConsultation> Outbound,
        IReadOnlyList<PushDelivery> Deliveries);

    /// <summary>States a task can still be stopped in.</summary>
    private static readonly HashSet<string> Running =
        new(StringComparer.OrdinalIgnoreCase) { "submitted", "working", "input-required", "auth-required" };

    public static IEndpointRouteBuilder MapA2AAdmin(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/admin/a2a").RequireAuthorization(AuthPolicies.FirmAdmin);

        api.MapGet("", async (IPrincipalAccessor principals, IDbContextFactory<MafDbContext> db, CancellationToken ct) =>
        {
            var principal = principals.Current;
            await using var context = await db.CreateDbContextAsync(ct);

            // A task carries the firm its partner was entitled to act for, stamped when it was created. The audit
            // would have been the other candidate, but it is written when a request *finishes*, so a task still
            // running would be invisible — and an operator's first question is about exactly those.
            var tasks = await context.A2ATasks.AsNoTracking()
                .Where(t => t.FirmId == principal.FirmId.Value)
                .OrderByDescending(t => t.UpdatedAt)
                .Take(100)
                .ToListAsync(ct);

            var taskIds = tasks.Select(t => t.Id).ToList();

            // The audit says what each request was and how long it took, for the ones that have finished.
            var records = await context.Audit
                .Where(a => a.FirmId == principal.FirmId.Value
                    && (a.Kind == AuditKinds.A2ARequest || a.Kind == AuditKinds.A2AConsultation))
                .OrderByDescending(a => a.Id)
                .Take(500)
                .ToListAsync(ct);

            var byTask = records
                .Where(a => a.Kind == AuditKinds.A2ARequest && TaskIdOf(a.Arguments) is { Length: > 0 })
                .GroupBy(a => TaskIdOf(a.Arguments)!)
                .ToDictionary(g => g.Key, g => g.First());

            var inbound = tasks.Select(t => new InboundTask(
                t.Id,
                t.PartnerId ?? "unknown",
                byTask.TryGetValue(t.Id, out var record) ? record.ToolName : "a2a.message",
                t.State,
                new DateTimeOffset(t.CreatedAt, TimeSpan.Zero),
                new DateTimeOffset(t.UpdatedAt, TimeSpan.Zero),
                record?.DurationMs ?? 0,
                Running.Contains(t.State))).ToList();

            var outbound = records
                .Where(a => a.Kind == AuditKinds.A2AConsultation)
                .Select(a => new OutboundConsultation(
                    TaskIdOf(a.Arguments) ?? "-", AgentOf(a.Arguments), a.Outcome, a.DurationMs,
                    new DateTimeOffset(a.At, TimeSpan.Zero)))
                .ToList();

            var deliveries = await context.A2APushDeliveries.AsNoTracking()
                .Where(d => taskIds.Contains(d.TaskId))
                .OrderByDescending(d => d.At)
                .Take(100)
                .Select(d => new PushDelivery(d.TaskId, d.State, d.Url, d.Attempts, d.Delivered, d.Error,
                    new DateTimeOffset(d.At, TimeSpan.Zero)))
                .ToListAsync(ct);

            return Results.Ok(new A2AActivity(inbound, outbound, deliveries));
        });

        // The firm whose data is being worked on may stop the work. It ends the way a partner's cancel ends,
        // through the same server, so the task's final state and its events are the same either way.
        api.MapPost("/tasks/{id}/cancel", async (string id, IPrincipalAccessor principals,
            IDbContextFactory<MafDbContext> db, global::A2A.A2AServer server, Agent.ToolAudit audit,
            TimeProvider time, CancellationToken ct) =>
        {
            var principal = principals.Current;
            await using var context = await db.CreateDbContextAsync(ct);

            var row = await context.A2ATasks.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == id && t.FirmId == principal.FirmId.Value, ct);
            if (row is null)
            {
                return Results.NotFound();
            }
            if (!Running.Contains(row.State))
            {
                return Results.Conflict(new { state = row.State, message = "That task has already finished." });
            }

            var started = time.GetUtcNow();
            var cancelled = await server.CancelTaskAsync(new global::A2A.CancelTaskRequest { Id = id }, ct);
            var state = cancelled.Status?.State.ToString() ?? "Canceled";

            // Stopping another system's work is an action, and an action is recorded — naming who did it.
            await audit.RecordAsync(new Agent.AuditEntry(
                principal, null, null, "a2a.cancel", $"taskId={id} partner={row.PartnerId ?? "-"}", state,
                (long)(time.GetUtcNow() - started).TotalMilliseconds, AuditKinds.A2ARequest), ct);

            return Results.Ok(new { taskId = id, state });
        });

        return app;
    }

    /// <summary>The audit's arguments are `key=value` pairs; these are the two this screen reads.</summary>
    private static string? TaskIdOf(string arguments) => Value(arguments, "taskId");

    private static string AgentOf(string arguments) => Value(arguments, "agent") ?? "compliance";

    private static string? Value(string arguments, string key)
    {
        foreach (var part in arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.StartsWith($"{key}=", StringComparison.Ordinal))
            {
                var value = part[(key.Length + 1)..];
                return value is "-" or "" ? null : value;
            }
        }
        return null;
    }
}
