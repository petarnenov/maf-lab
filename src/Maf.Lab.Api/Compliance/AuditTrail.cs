using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Maf.Lab.Api.Agent;
using Maf.Lab.Api.Storage;
using Maf.Lab.Plugins.Abstractions;
using Maf.Lab.Retrieval.Auth;
using Microsoft.EntityFrameworkCore;

namespace Maf.Lab.Api.Compliance;

/// <summary>
/// The audit record as a screen reads it (<see cref="IAuditTrail"/>, extract-compliance-plugin): what an authorised
/// person can be handed when they ask, and how they can check the record was not altered afterwards. The records, the
/// chain's rule and the export's own record are the core's; the screen is a plugin's. The tenant always comes from the
/// request's principal — never from a parameter.
/// </summary>
public sealed class CoreAuditTrail(IPrincipalAccessor principals, IDbContextFactory<MafDbContext> db, ToolAudit audit, TimeProvider time)
    : IAuditTrail
{
    public async Task<AuditChainReport> VerifyAsync(CancellationToken ct)
    {
        await using var ctx = await db.CreateDbContextAsync(ct);
        // The chain is global, so it is walked whole; the report names a break wherever it falls.
        var rows = await ctx.Audit.AsNoTracking().OrderBy(a => a.Id).ToListAsync(ct);
        _ = principals.Current;
        var r = AuditChain.Verify(rows);
        return new AuditChainReport(r.Intact, r.Checked, r.Unchained, r.From, r.To, r.Head, r.FirstBrokenId, r.Reason);
    }

    public async Task<ActionPage> PageAsync(AuditFilter filter, CancellationToken ct)
    {
        var tenantId = principals.Current.TenantId.Value; // from the token, as the export is
        var take = Math.Clamp(filter.Limit, 1, 200);
        await using var ctx = await db.CreateDbContextAsync(ct);

        var query = ctx.Audit.AsNoTracking().Where(a => a.TenantId == tenantId);
        if (filter.From is { } start)
        {
            var at = start.UtcDateTime;
            query = query.Where(a => a.At >= at);
        }
        if (filter.To is { } end)
        {
            var at = end.UtcDateTime;
            query = query.Where(a => a.At <= at);
        }
        if (!string.IsNullOrWhiteSpace(filter.UserId))
        {
            query = query.Where(a => a.PrincipalId == filter.UserId);
        }
        if (!string.IsNullOrWhiteSpace(filter.Kind))
        {
            query = query.Where(a => a.Kind == filter.Kind);
        }
        // Newest first, paged by row id: the same order the chain follows, and it cannot drift with clocks.
        if (filter.Before is { } cursor)
        {
            query = query.Where(a => a.Id < cursor);
        }

        var page = await query.OrderByDescending(a => a.Id).Take(take + 1).ToListAsync(ct);
        var more = page.Count > take;
        var actions = page.Take(take)
            .Select(a => new ExportedAction(a.Id, Utc(a.At), a.PrincipalId, a.Kind, a.ToolName, a.Arguments,
                a.Outcome, a.DurationMs, a.ConversationId, a.TurnId, a.Hash))
            .ToList();
        return new ActionPage(actions, more ? actions[^1].Id : null);
    }

    public async Task<ExportPackage> ExportAsync(ExportRange range, CancellationToken ct)
    {
        var principal = principals.Current;
        var tenantId = principal.TenantId.Value; // from the token: no parameter can reach another firm
        var (start, end, userId) = (range.From.UtcDateTime, range.To.UtcDateTime, range.UserId);
        await using var ctx = await db.CreateDbContextAsync(ct);

        var conversations = await ctx.Conversations.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.CreatedAt >= start && c.CreatedAt <= end)
            .Where(c => userId == null || c.UserId == userId)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new ExportedConversation(c.Id, c.UserId, Utc(c.CreatedAt), Utc(c.LastActivityAt), c.Title,
                c.DeletedAt == null ? null : Utc(c.DeletedAt.Value)))
            .ToListAsync(ct);

        var turns = await ctx.Turns.AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.CreatedAt >= start && t.CreatedAt <= end)
            .Where(t => userId == null || t.UserId == userId)
            .OrderBy(t => t.CreatedAt)
            .Select(t => new ExportedTurn(t.Id, t.ConversationId, t.UserId, Utc(t.CreatedAt), t.Question, t.Answer,
                t.Intent, t.ForcedRetrieval, t.ToolCallsJson, t.SourcesJson, t.SignalsJson))
            .ToListAsync(ct);

        var actions = await ctx.Audit.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.At >= start && a.At <= end)
            .Where(a => userId == null || a.PrincipalId == userId)
            .OrderBy(a => a.Id)
            .Select(a => new ExportedAction(a.Id, Utc(a.At), a.PrincipalId, a.Kind, a.ToolName, a.Arguments,
                a.Outcome, a.DurationMs, a.ConversationId, a.TurnId, a.Hash))
            .ToListAsync(ct);

        // The head as of *before* this export's own row: the package cannot contain its own digest.
        var head = await audit.HeadAsync(ct);
        var counts = new Dictionary<string, int>
        {
            ["conversations"] = conversations.Count,
            ["turns"] = turns.Count,
            ["actions"] = actions.Count,
        };
        var content = new ExportContent(conversations, turns, actions);
        var manifest = new ExportManifest(tenantId, userId, new DateTimeOffset(start, TimeSpan.Zero),
            new DateTimeOffset(end, TimeSpan.Zero), time.GetUtcNow(), principal.UserId, counts,
            Digest(content), head);

        // Recorded before the package is returned: a failed download still leaves the attempt in the record.
        await audit.RecordAsync(new AuditEntry(principal, null, null, AuditKinds.ComplianceExport,
            $"from={start:yyyy-MM-dd} to={end:yyyy-MM-dd}"
            + (userId is null ? "" : $" subject={userId}")
            + $" conversations={counts["conversations"]} turns={counts["turns"]} actions={counts["actions"]}",
            "ok", 0, AuditKinds.ComplianceExport), ct);

        return new ExportPackage(manifest, content.Conversations, content.Turns, content.Actions);
    }

    /// <summary>
    /// A digest over a canonical rendering of the content — not over this process's JSON output, which only this
    /// serialiser can reproduce. A recipient in any language can rebuild these lines from the package and re-hash.
    /// Fields are joined with U+001F, rows with U+000A, sections in this order behind a named header.
    /// </summary>
    internal static string Digest(ExportContent content)
    {
        var text = new StringBuilder();
        text.Append("conversations\n");
        foreach (var c in content.Conversations)
        {
            Row(text, c.ConversationId, c.UserId, Iso(c.CreatedAt), Iso(c.LastActivityAt), c.Title ?? "",
                c.DeletedAt is null ? "" : Iso(c.DeletedAt.Value));
        }
        text.Append("turns\n");
        foreach (var t in content.Turns)
        {
            Row(text, t.TurnId, t.ConversationId, t.UserId, Iso(t.CreatedAt), t.Question, t.Answer, t.Intent,
                t.ForcedRetrieval ? "true" : "false", t.ToolCallsJson, t.SourcesJson, t.SignalsJson);
        }
        text.Append("actions\n");
        foreach (var a in content.Actions)
        {
            Row(text, a.Id.ToString(CultureInfo.InvariantCulture), Iso(a.At), a.PrincipalId, a.Kind ?? "", a.Action,
                a.Arguments, a.Outcome, a.DurationMs.ToString(CultureInfo.InvariantCulture),
                a.ConversationId ?? "", a.TurnId ?? "", a.Hash ?? "");
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))).ToLowerInvariant();
    }

    private static void Row(StringBuilder text, params string[] fields) =>
        text.Append(string.Join('\u001f', fields)).Append('\n');

    /// <summary>
    /// UTC to millisecond precision. Deliberately not "O": .NET renders 100-nanosecond ticks, which a recipient
    /// whose language has microsecond timestamps cannot reproduce — and a digest nobody else can recompute is not a
    /// digest worth publishing.
    /// </summary>
    private static string Iso(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static DateTimeOffset Utc(DateTime at) => new(DateTime.SpecifyKind(at, DateTimeKind.Utc));

}
