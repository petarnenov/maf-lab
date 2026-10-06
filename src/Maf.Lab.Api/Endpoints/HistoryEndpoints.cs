using System.Text.Json;
using Maf.Lab.Api.Agent;
using Maf.Lab.Api.Compliance;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Chat;
using Maf.Lab.Domain.Feedback;
using Maf.Lab.Domain.History;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Retrieval.Auth;
using Microsoft.EntityFrameworkCore;

namespace Maf.Lab.Api.Endpoints;

/// <summary>
/// A conversation reopened: the caller's own only (user and tenant must match); deleted ones are gone. The list, rename
/// and delete are the conversation-history plugin's, over <see cref="Maf.Lab.Plugins.Abstractions.IConversationStore"/>.
/// </summary>
public static class HistoryEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapHistory(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/conversations").RequireAuthorization();

        api.MapGet("/{id}", async (string id, IPrincipalAccessor principals, IDbContextFactory<MafDbContext> db, CancellationToken ct) =>
        {
            var p = principals.Current;
            await using var ctx = await db.CreateDbContextAsync(ct);
            var conversation = await Owned(ctx, p).FirstOrDefaultAsync(c => c.Id == id, ct);
            if (conversation is null)
            {
                return Results.NotFound();
            }
            var turns = await ctx.Turns.AsNoTracking().Where(t => t.ConversationId == id).OrderBy(t => t.CreatedAt).ToListAsync(ct);
            var turnIds = turns.Select(t => t.Id).ToList();
            var feedback = await ctx.Feedback.AsNoTracking().Where(f => turnIds.Contains(f.TurnId) && f.UserId == p.UserId)
                .Select(f => new { f.TurnId, f.Kind }).ToListAsync(ct);

            var history = turns.Select(t => new HistoryTurn(
                t.Id, t.Question, t.Answer, Utc(t.CreatedAt),
                (JsonSerializer.Deserialize<List<ToolCallRecord>>(t.ToolCallsJson, Json) ?? [])
                    .Select(c => new HistoryToolCall(c.CallId, c.ToolName, c.ArgumentSummary, c.Outcome, c.ResultSummary, c.SourceCount)).ToList(),
                ReadSources(t.SourcesJson),
                feedback.Where(f => f.TurnId == t.Id).Select(f => f.Kind).Distinct().ToList(),
                ReadActivities(t.ActivitiesJson),
                string.IsNullOrEmpty(t.Reasoning) ? null : t.Reasoning,
                string.IsNullOrEmpty(t.Reasoning) ? null : t.ReasoningMs)).ToList();

            var title = conversation.Title ?? ConversationTitles.FromQuestion(turns.FirstOrDefault()?.Question ?? "");
            return Results.Ok(new ConversationDetail(conversation.Id, title, Utc(conversation.CreatedAt), Utc(conversation.LastActivityAt), history,
                conversation.FocusAccountId is { } focus ? new ConversationFocus(focus) : null));
        });

        // What this conversation is waiting on, so a proposal outlives the page that made it. The run is gone;
        // the proposal is not, and approving twice applies once either way.
        api.MapGet("/{id}/pending", async (string id, IPrincipalAccessor principals, IDbContextFactory<MafDbContext> db,
            TimeProvider time, CancellationToken ct) =>
        {
            var principal = principals.Current;
            await using var context = await db.CreateDbContextAsync(ct);
            var owns = await context.Conversations.AnyAsync(
                c => c.Id == id && c.UserId == principal.UserId && c.TenantId == principal.TenantId.Value && c.DeletedAt == null, ct);
            if (!owns)
            {
                return Results.NotFound();
            }

            var row = await context.PendingAdjustments
                .Where(p => p.ConversationId == id
                    && p.UserId == principal.UserId
                    && p.TenantId == principal.TenantId.Value
                    && p.Status == PendingAdjustmentStatus.AwaitingConfirmation)
                .OrderByDescending(p => p.UpdatedAt)
                .FirstOrDefaultAsync(ct);

            if (row is null
                || JsonSerializer.Deserialize<Maf.Lab.Domain.Billing.FeeAdjustmentSummary>(row.Summary, Json) is not { } summary
                || (row.ExpiresAt is { } expiry && expiry <= time.GetUtcNow().UtcDateTime))
            {
                return Results.Ok(new { pending = (object?)null });
            }

            return Results.Ok(new
            {
                pending = new Maf.Lab.Domain.Billing.PendingProposal(
                    row.Id,
                    summary,
                    row.Question ?? "",
                    row.ExpiresAt is { } at ? new DateTimeOffset(at, TimeSpan.Zero) : null),
            });
        });

        return app;
    }

    private static IQueryable<ConversationRow> Owned(MafDbContext ctx, Principal p) =>
        ctx.Conversations.Where(c => c.UserId == p.UserId && c.TenantId == p.TenantId.Value && c.DeletedAt == null);

    /// <summary>Reads full SourceRefs; rows stored before chat history only have docId/sectionPath.</summary>
    private static List<SourceRef> ReadSources(string json)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json);
        // A stored code source keeps its place; one stored before add-codebase-domain reads as it always did.
        return doc.RootElement.EnumerateArray().Select(SourceRef.FromSearchItem).ToList();
    }

    /// <summary>A turn's data cards. A row stored before cards existed holds '' (the added column's default): no cards.</summary>
    private static List<HistoryActivity> ReadActivities(string json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<HistoryActivity>>(json, Json) ?? [];

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
