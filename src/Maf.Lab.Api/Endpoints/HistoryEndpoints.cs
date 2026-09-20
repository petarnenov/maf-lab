using System.Globalization;
using System.Text.Json;
using Maf.Lab.Api.Agent;
using Maf.Lab.Api.Compliance;
using Maf.Lab.Api.History;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Chat;
using Maf.Lab.Domain.Feedback;
using Maf.Lab.Domain.History;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Retrieval.Auth;
using Microsoft.EntityFrameworkCore;

namespace Maf.Lab.Api.Endpoints;

/// <summary>Chat history: the caller's own conversations only (user and firm must match); deleted ones are gone.</summary>
public static class HistoryEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapHistory(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/conversations").RequireAuthorization();

        api.MapGet("", async (string? search, int? limit, string? before, IPrincipalAccessor principals, IDbContextFactory<MafDbContext> db, CancellationToken ct) =>
        {
            var p = principals.Current;
            var take = Math.Clamp(limit ?? 30, 1, 100);
            await using var ctx = await db.CreateDbContextAsync(ct);

            var query = Owned(ctx, p).Where(c => ctx.Turns.Any(t => t.ConversationId == c.Id));
            if (!string.IsNullOrWhiteSpace(search))
            {
                var pattern = $"%{search.Trim().Replace("%", "").Replace("_", "")}%";
                query = query.Where(c => (c.Title != null && EF.Functions.Like(c.Title, pattern))
                    || ctx.Turns.Any(t => t.ConversationId == c.Id && (EF.Functions.Like(t.Question, pattern) || EF.Functions.Like(t.Answer, pattern))));
            }
            if (TryParseCursor(before, out var cursorAt, out var cursorId))
            {
                query = query.Where(c => c.LastActivityAt < cursorAt || (c.LastActivityAt == cursorAt && string.Compare(c.Id, cursorId) < 0));
            }

            var rows = await query.OrderByDescending(c => c.LastActivityAt).ThenByDescending(c => c.Id).Take(take + 1)
                .Select(c => new
                {
                    c.Id, c.Title, c.CreatedAt, c.LastActivityAt,
                    TurnCount = ctx.Turns.Count(t => t.ConversationId == c.Id),
                    FirstQuestion = ctx.Turns.Where(t => t.ConversationId == c.Id).OrderBy(t => t.CreatedAt).Select(t => t.Question).FirstOrDefault(),
                })
                .ToListAsync(ct);

            var page = rows.Take(take).Select(r => new ConversationSummary(r.Id, r.Title ?? ConversationTitles.FromQuestion(r.FirstQuestion ?? ""),
                Utc(r.CreatedAt), Utc(r.LastActivityAt), r.TurnCount)).ToList();
            var next = rows.Count > take ? Cursor(rows[take - 1].LastActivityAt, rows[take - 1].Id) : null;
            return Results.Ok(new ConversationPage(page, next));
        });

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
            var traced = (await ctx.TurnTraces.AsNoTracking().Where(t => turnIds.Contains(t.TurnId)).Select(t => t.TurnId).ToListAsync(ct)).ToHashSet();

            var history = turns.Select(t => new HistoryTurn(
                t.Id, t.Question, t.Answer, Utc(t.CreatedAt),
                (JsonSerializer.Deserialize<List<ToolCallRecord>>(t.ToolCallsJson, Json) ?? [])
                    .Select(c => new HistoryToolCall(c.CallId, c.ToolName, c.ArgumentSummary, c.Outcome, c.ResultSummary, c.SourceCount)).ToList(),
                ReadSources(t.SourcesJson),
                feedback.Where(f => f.TurnId == t.Id).Select(f => f.Kind).Distinct().ToList(),
                traced.Contains(t.Id))).ToList();

            var title = conversation.Title ?? ConversationTitles.FromQuestion(turns.FirstOrDefault()?.Question ?? "");
            return Results.Ok(new ConversationDetail(conversation.Id, title, Utc(conversation.CreatedAt), Utc(conversation.LastActivityAt), history));
        });

        // What this conversation is waiting on, so a proposal outlives the page that made it. The run is gone;
        // the proposal is not, and approving twice applies once either way.
        api.MapGet("/{id}/pending", async (string id, IPrincipalAccessor principals, IDbContextFactory<MafDbContext> db,
            TimeProvider time, CancellationToken ct) =>
        {
            var principal = principals.Current;
            await using var context = await db.CreateDbContextAsync(ct);
            var owns = await context.Conversations.AnyAsync(
                c => c.Id == id && c.UserId == principal.UserId && c.FirmId == principal.FirmId.Value && c.DeletedAt == null, ct);
            if (!owns)
            {
                return Results.NotFound();
            }

            var row = await context.PendingAdjustments
                .Where(p => p.ConversationId == id
                    && p.UserId == principal.UserId
                    && p.FirmId == principal.FirmId.Value
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

        api.MapPatch("/{id}", async (string id, RenameConversationRequest request, IPrincipalAccessor principals, IDbContextFactory<MafDbContext> db, CancellationToken ct) =>
        {
            var title = ConversationTitles.Validate(request.Title);
            if (title is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["title"] = [$"title must be 1–{ConversationTitles.MaxChars} characters."] });
            }
            await using var ctx = await db.CreateDbContextAsync(ct);
            var conversation = await Owned(ctx, principals.Current).FirstOrDefaultAsync(c => c.Id == id, ct);
            if (conversation is null)
            {
                return Results.NotFound();
            }
            conversation.Title = title;
            await ctx.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        api.MapDelete("/{id}", async (string id, IPrincipalAccessor principals, IDbContextFactory<MafDbContext> db, TimeProvider time,
            ToolAudit audit, CancellationToken ct) =>
        {
            await using var ctx = await db.CreateDbContextAsync(ct);
            var conversation = await Owned(ctx, principals.Current).FirstOrDefaultAsync(c => c.Id == id, ct);
            if (conversation is null)
            {
                // Nothing was deleted, so nothing is attributed to the caller as a deletion.
                return Results.NotFound();
            }
            conversation.DeletedAt = time.GetUtcNow().UtcDateTime; // soft delete: turns stay for the review queue and evals
            await ctx.SaveChangesAsync(ct);
            // Destroying data is the action an investigator asks about first: it belongs in the record.
            await audit.RecordAsync(new AuditEntry(principals.Current, id, null, AuditKinds.ConversationDelete,
                $"conversationId={id}", "ok", 0, AuditKinds.ConversationDelete), ct);
            return Results.NoContent();
        });

        return app;
    }

    private static IQueryable<ConversationRow> Owned(MafDbContext ctx, Principal p) =>
        ctx.Conversations.Where(c => c.UserId == p.UserId && c.FirmId == p.FirmId.Value && c.DeletedAt == null);

    /// <summary>Reads full SourceRefs; rows stored before chat history only have docId/sectionPath.</summary>
    private static List<SourceRef> ReadSources(string json)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json);
        return doc.RootElement.EnumerateArray().Select(e => new SourceRef(
            Str(e, "docId"), Str(e, "sectionPath"), Str(e, "sourcePath"), Str(e, "snippet"))).ToList();
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static string Cursor(DateTime at, string id) => $"{at.Ticks.ToString(CultureInfo.InvariantCulture)}:{id}";

    private static bool TryParseCursor(string? cursor, out DateTime at, out string id)
    {
        at = default;
        id = "";
        var parts = cursor?.Split(':', 2);
        if (parts is not { Length: 2 } || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks))
        {
            return false;
        }
        at = new DateTime(ticks, DateTimeKind.Utc);
        id = parts[1];
        return true;
    }
}
