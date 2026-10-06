using System.Globalization;
using Maf.Lab.Api.Agent;
using Maf.Lab.Api.Compliance;
using Maf.Lab.Domain.History;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Plugins.Abstractions;
using Maf.Lab.Retrieval.Auth;
using Microsoft.EntityFrameworkCore;

namespace Maf.Lab.Api.Storage;

/// <summary>
/// The core's side of <see cref="IConversationStore"/> (decision 5y): the caller's own conversations (user and tenant
/// must match, deleted ones are gone), read from the request's principal. What the list plugin shows, it reads here.
/// </summary>
public sealed class ConversationStore(IPrincipalAccessor principals, IDbContextFactory<MafDbContext> db, TimeProvider time, ToolAudit audit)
    : IConversationStore
{
    public async Task<ConversationPage> PageAsync(string? search, int limit, string? before, CancellationToken ct)
    {
        var take = Math.Clamp(limit, 1, 100);
        await using var ctx = await db.CreateDbContextAsync(ct);

        var query = Owned(ctx, principals.Current).Where(c => ctx.Turns.Any(t => t.ConversationId == c.Id));
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
        return new ConversationPage(page, next);
    }

    public async Task<RenameOutcome> RenameAsync(string conversationId, string title, CancellationToken ct)
    {
        // The plugin validates first; this is the store's own guard.
        if (ConversationTitles.Validate(title) is not { } valid)
        {
            return RenameOutcome.Invalid;
        }
        await using var ctx = await db.CreateDbContextAsync(ct);
        var conversation = await Owned(ctx, principals.Current).FirstOrDefaultAsync(c => c.Id == conversationId, ct);
        if (conversation is null)
        {
            return RenameOutcome.NotFound;
        }
        conversation.Title = valid;
        await ctx.SaveChangesAsync(ct);
        return RenameOutcome.Renamed;
    }

    public async Task<bool> DeleteAsync(string conversationId, CancellationToken ct)
    {
        var principal = principals.Current;
        await using var ctx = await db.CreateDbContextAsync(ct);
        var conversation = await Owned(ctx, principal).FirstOrDefaultAsync(c => c.Id == conversationId, ct);
        if (conversation is null)
        {
            // Nothing was deleted, so nothing is attributed to the caller as a deletion.
            return false;
        }
        conversation.DeletedAt = time.GetUtcNow().UtcDateTime; // soft delete: turns stay for the review queue and evals
        await ctx.SaveChangesAsync(ct);
        // Destroying data is the action an investigator asks about first: it belongs in the record.
        await audit.RecordAsync(new AuditEntry(principal, conversationId, null, AuditKinds.ConversationDelete,
            $"conversationId={conversationId}", "ok", 0, AuditKinds.ConversationDelete), ct);
        return true;
    }

    private static IQueryable<ConversationRow> Owned(MafDbContext ctx, Principal p) =>
        ctx.Conversations.Where(c => c.UserId == p.UserId && c.TenantId == p.TenantId.Value && c.DeletedAt == null);

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
