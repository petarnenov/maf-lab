using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Maf.Lab.Api.Agent;
using Maf.Lab.Api.Compliance;
using Maf.Lab.Api.Storage;
using Maf.Lab.Retrieval.Auth;
using Microsoft.EntityFrameworkCore;

namespace Maf.Lab.Api.Endpoints;

/// <summary>
/// What an authorised person can be handed when they ask, and how they can check the record was not altered
/// afterwards. The firm always comes from the token — never from a parameter.
/// </summary>
public static class ComplianceEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapCompliance(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/admin/compliance").RequireAuthorization(AuthPolicies.FirmAdmin);

        api.MapGet("/verify", async (IPrincipalAccessor principals, IDbContextFactory<MafDbContext> db, CancellationToken ct) =>
        {
            await using var ctx = await db.CreateDbContextAsync(ct);
            // The chain is global, so it is walked whole; the report names a break wherever it falls.
            var rows = await ctx.Audit.AsNoTracking().OrderBy(a => a.Id).ToListAsync(ct);
            _ = principals.Current;
            return Results.Ok(AuditChain.Verify(rows));
        });

        // Reading the record is deliberately not recorded: browsing the log must not grow the log.
        api.MapGet("/actions", async (DateTimeOffset? from, DateTimeOffset? to, string? userId, string? kind,
            int? limit, long? before, IPrincipalAccessor principals, IDbContextFactory<MafDbContext> db, CancellationToken ct) =>
        {
            var firmId = principals.Current.FirmId.Value; // from the token, as the export is
            var take = Math.Clamp(limit ?? 50, 1, 200);
            await using var ctx = await db.CreateDbContextAsync(ct);

            var query = ctx.Audit.AsNoTracking().Where(a => a.FirmId == firmId);
            if (from is { } start)
            {
                var at = start.UtcDateTime;
                query = query.Where(a => a.At >= at);
            }
            if (to is { } end)
            {
                var at = end.UtcDateTime;
                query = query.Where(a => a.At <= at);
            }
            if (!string.IsNullOrWhiteSpace(userId))
            {
                query = query.Where(a => a.PrincipalId == userId);
            }
            if (!string.IsNullOrWhiteSpace(kind))
            {
                query = query.Where(a => a.Kind == kind);
            }
            // Newest first, paged by row id: the same order the chain follows, and it cannot drift with clocks.
            if (before is { } cursor)
            {
                query = query.Where(a => a.Id < cursor);
            }

            var page = await query.OrderByDescending(a => a.Id).Take(take + 1).ToListAsync(ct);
            var more = page.Count > take;
            var actions = page.Take(take)
                .Select(a => new ExportedAction(a.Id, Utc(a.At), a.PrincipalId, a.Kind, a.ToolName, a.Arguments,
                    a.Outcome, a.DurationMs, a.ConversationId, a.TurnId, a.Hash))
                .ToList();
            return Results.Ok(new ActionPage(actions, more ? actions[^1].Id : null));
        });

        api.MapGet("/export", async (DateTimeOffset? from, DateTimeOffset? to, string? userId,
            IPrincipalAccessor principals, IDbContextFactory<MafDbContext> db, ToolAudit audit, TimeProvider time,
            CancellationToken ct) =>
        {
            var principal = principals.Current;
            var firmId = principal.FirmId.Value; // from the token: no parameter can reach another firm
            var start = (from ?? DateTimeOffset.UtcNow.AddYears(-1)).UtcDateTime;
            var end = (to ?? DateTimeOffset.UtcNow).UtcDateTime;
            if (end < start)
            {
                return Results.BadRequest(new { error = "'to' is before 'from'" });
            }

            await using var ctx = await db.CreateDbContextAsync(ct);

            var conversations = await ctx.Conversations.AsNoTracking()
                .Where(c => c.FirmId == firmId && c.CreatedAt >= start && c.CreatedAt <= end)
                .Where(c => userId == null || c.UserId == userId)
                .OrderBy(c => c.CreatedAt)
                .Select(c => new ExportedConversation(c.Id, c.UserId, Utc(c.CreatedAt), Utc(c.LastActivityAt), c.Title,
                    c.DeletedAt == null ? null : Utc(c.DeletedAt.Value)))
                .ToListAsync(ct);

            var turns = await ctx.Turns.AsNoTracking()
                .Where(t => t.FirmId == firmId && t.CreatedAt >= start && t.CreatedAt <= end)
                .Where(t => userId == null || t.UserId == userId)
                .OrderBy(t => t.CreatedAt)
                .Select(t => new ExportedTurn(t.Id, t.ConversationId, t.UserId, Utc(t.CreatedAt), t.Question, t.Answer,
                    t.Intent, t.ForcedRetrieval, t.ToolCallsJson, t.SourcesJson, t.SignalsJson))
                .ToListAsync(ct);

            var actions = await ctx.Audit.AsNoTracking()
                .Where(a => a.FirmId == firmId && a.At >= start && a.At <= end)
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
            var manifest = new ExportManifest(firmId, userId, new DateTimeOffset(start, TimeSpan.Zero),
                new DateTimeOffset(end, TimeSpan.Zero), time.GetUtcNow(), principal.UserId, counts,
                Digest(content), head);

            // Recorded before the package is returned: a failed download still leaves the attempt in the record.
            await audit.RecordAsync(new AuditEntry(principal, null, null, AuditKinds.ComplianceExport,
                $"from={start:yyyy-MM-dd} to={end:yyyy-MM-dd}"
                + (userId is null ? "" : $" subject={userId}")
                + $" conversations={counts["conversations"]} turns={counts["turns"]} actions={counts["actions"]}",
                "ok", 0, AuditKinds.ComplianceExport), ct);

            return Results.Ok(new ExportPackage(manifest, content.Conversations, content.Turns, content.Actions));
        });

        return api;
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

public sealed record ExportedConversation(string ConversationId, string UserId, DateTimeOffset CreatedAt,
    DateTimeOffset LastActivityAt, string? Title, DateTimeOffset? DeletedAt);

public sealed record ExportedTurn(string TurnId, string ConversationId, string UserId, DateTimeOffset CreatedAt,
    string Question, string Answer, string Intent, bool ForcedRetrieval, string ToolCallsJson, string SourcesJson, string SignalsJson);

public sealed record ExportedAction(long Id, DateTimeOffset At, string PrincipalId, string? Kind, string Action,
    string Arguments, string Outcome, long DurationMs, string? ConversationId, string? TurnId, string? Hash);

/// <summary>Exactly what the digest covers.</summary>
public sealed record ExportContent(IReadOnlyList<ExportedConversation> Conversations, IReadOnlyList<ExportedTurn> Turns,
    IReadOnlyList<ExportedAction> Actions);

/// <param name="Sha256">Digest over the content sections, so the package can be checked by someone who did not produce it.</param>
/// <param name="AuditChainHead">The record's head at the moment the package was drawn.</param>
public sealed record ExportManifest(string FirmId, string? SubjectUserId, DateTimeOffset From, DateTimeOffset To,
    DateTimeOffset GeneratedAt, string By, IReadOnlyDictionary<string, int> Counts, string Sha256, string? AuditChainHead);

/// <param name="NextCursor">Pass as `before` for the next, older page; null when there are no more.</param>
public sealed record ActionPage(IReadOnlyList<ExportedAction> Actions, long? NextCursor);

public sealed record ExportPackage(ExportManifest Manifest, IReadOnlyList<ExportedConversation> Conversations,
    IReadOnlyList<ExportedTurn> Turns, IReadOnlyList<ExportedAction> Actions);
