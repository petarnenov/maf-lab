using System.Data;
using System.Globalization;
using Maf.Lab.Api.Agent;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.SharedState;
using Maf.Lab.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Maf.Lab.Api.Compliance;

public sealed record ContentGrantDto(long Id, string OperatorId, string Reason, DateTimeOffset StartedAt,
    DateTimeOffset ExpiresAt, DateTimeOffset? EndRequestedAt, DateTimeOffset? EndedAt);
public sealed record ContentGrantEnvelope(ContentGrantDto? Grant, bool Active);
public sealed record ContentGrantPage(IReadOnlyList<ContentGrantDto> Grants, long? NextCursor);
public enum ContentGrantStatus { Created, Conflict, Invalid, Unavailable, Ended, NotFound }
public sealed record ContentGrantChange(ContentGrantStatus Status, ContentGrantEnvelope Envelope);

/// <summary>Commits evidence before enabling content, and revokes content before acknowledging the end.</summary>
public sealed class ContentAccessGrants(IDbContextFactory<MafDbContext> database, ToolAudit audit,
    IBreakGlassPermissionStore permissions, TimeProvider clock)
{
    public const string StartedKind = "operator.content-access.start";
    public const string EndRequestedKind = "operator.content-access.end-request";
    public const string EndedKind = "operator.content-access.end";

    public async Task<ContentGrantChange> IssueAsync(Principal actor, string sessionKey, string? reason,
        int durationMinutes, CancellationToken ct)
    {
        RequireOperator(actor, sessionKey);
        if (!ValidReason(reason) || durationMinutes is < 1 or > 60)
            return new(ContentGrantStatus.Invalid, new(null, false));
        await ReconcileSessionAsync(actor, sessionKey, ct);
        ContentAccessGrantRow row;
        await using (var db = await database.CreateDbContextAsync(ct))
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var existing = await db.ContentAccessGrants.AsNoTracking()
                .SingleOrDefaultAsync(grant => grant.SessionKey == sessionKey && grant.EndedAt == null, ct);
            if (existing is not null)
                return new(ContentGrantStatus.Conflict, new(Dto(existing), false));
            var now = clock.GetUtcNow().UtcDateTime;
            row = new ContentAccessGrantRow
            {
                SessionKey = sessionKey, OperatorId = actor.UserId, TenantId = actor.TenantId.Value,
                Reason = reason!, StartedAt = now, ExpiresAt = now.AddMinutes(durationMinutes),
            };
            db.ContentAccessGrants.Add(row);
            // Allocate the identifier inside the same transaction; a failed audit also rolls back this insert.
            await db.SaveChangesAsync(ct);
            await audit.RecordInTransactionAsync(db, Entry(actor, row, StartedKind, "started"), ct);
            await transaction.CommitAsync(ct);
        }
        var active = await ActivateAsync(row, ct) && await IsAllowedAsync(actor, sessionKey, ct);
        return new(active ? ContentGrantStatus.Created : ContentGrantStatus.Unavailable, new(Dto(row), active));
    }

    public async Task<ContentGrantEnvelope> ReadAsync(Principal actor, string sessionKey, CancellationToken ct)
    {
        RequireOperator(actor, sessionKey);
        await ReconcileSessionAsync(actor, sessionKey, ct);
        await using var db = await database.CreateDbContextAsync(ct);
        var row = await db.ContentAccessGrants.AsNoTracking()
            .Where(grant => grant.SessionKey == sessionKey && grant.OperatorId == actor.UserId
                && grant.TenantId == actor.TenantId.Value).OrderByDescending(grant => grant.Id).FirstOrDefaultAsync(ct);
        if (row is null) return new(null, false);
        var active = await ActivateAsync(row, ct) && await IsAllowedAsync(actor, sessionKey, ct);
        return new(Dto(row), active);
    }

    public async Task<bool> IsAllowedAsync(Principal actor, string sessionKey, CancellationToken ct)
    {
        RequireOperator(actor, sessionKey);
        ContentPermission? permission;
        try { permission = await permissions.ReadAsync(sessionKey, ct); }
        catch (Exception) when (!ct.IsCancellationRequested) { return false; }
        if (permission is null || permission.ExpiresAt <= clock.GetUtcNow()) return false;
        await using var db = await database.CreateDbContextAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var row = await db.ContentAccessGrants.AsNoTracking().SingleOrDefaultAsync(grant => grant.SessionKey == sessionKey
            && grant.OperatorId == actor.UserId && grant.TenantId == actor.TenantId.Value && grant.EndedAt == null
            && grant.EndRequestedAt == null && grant.ExpiresAt > now, ct);
        return row is not null && permission.GrantId == row.Id.ToString(CultureInfo.InvariantCulture)
            && permission.ExpiresAt == Utc(row.ExpiresAt);
    }

    public async Task<ContentGrantChange> EndAsync(Principal actor, string sessionKey, long id, CancellationToken ct)
    {
        RequireOperator(actor, sessionKey);
        var row = await RequestEndAsync(actor, sessionKey, id, ct);
        if (row is null) return new(ContentGrantStatus.NotFound, new(null, false));
        if (row.EndedAt is not null) return new(ContentGrantStatus.Ended, new(Dto(row), false));
        var ended = await FinishEndAsync(row, ct);
        return new(ended ? ContentGrantStatus.Ended : ContentGrantStatus.Unavailable, new(Dto(row), false));
    }

    public async Task<ContentGrantPage> ListAsync(Principal actor, long? before, CancellationToken ct)
    {
        if (actor.Role != Role.TENANT_ADMIN || actor.TenantId.IsShared)
            throw new UnauthorizedAccessException("Only the selected tenant's admin may read content grants.");
        await using var db = await database.CreateDbContextAsync(ct);
        var rows = await db.ContentAccessGrants.AsNoTracking().Where(row => row.TenantId == actor.TenantId.Value
            && (!before.HasValue || row.Id < before.Value)).OrderByDescending(row => row.Id).Take(51).ToListAsync(ct);
        return new(rows.Take(50).Select(Dto).ToArray(), rows.Count > 50 ? rows[49].Id : null);
    }

    /// <summary>Expiry is enforced by the permission TTL; this independent loop completes its durable audit.</summary>
    public async Task ReconcileAsync(CancellationToken ct)
    {
        await using var db = await database.CreateDbContextAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var rows = await db.ContentAccessGrants.AsNoTracking()
            .Where(row => row.EndedAt == null && (row.EndRequestedAt != null || row.ExpiresAt <= now))
            .OrderBy(row => row.Id).Take(100).ToListAsync(ct);
        foreach (var row in rows)
        {
            var actor = new Principal(row.OperatorId, TenantId.Firm(row.TenantId), Role.PLATFORM_ADMIN);
            var ending = await RequestEndAsync(actor, row.SessionKey, row.Id, ct);
            if (ending is not null && ending.EndedAt is null) await FinishEndAsync(ending, ct);
        }
    }

    private async Task ReconcileSessionAsync(Principal actor, string sessionKey, CancellationToken ct)
    {
        await using var db = await database.CreateDbContextAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var row = await db.ContentAccessGrants.AsNoTracking().SingleOrDefaultAsync(grant => grant.SessionKey == sessionKey
            && grant.EndedAt == null && (grant.EndRequestedAt != null || grant.ExpiresAt <= now), ct);
        if (row is not null) await EndAsync(actor, sessionKey, row.Id, ct);
    }

    private async Task<ContentAccessGrantRow?> RequestEndAsync(Principal actor, string sessionKey, long id, CancellationToken ct)
    {
        await using var db = await database.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var row = await db.ContentAccessGrants.SingleOrDefaultAsync(grant => grant.Id == id && grant.SessionKey == sessionKey
            && grant.OperatorId == actor.UserId && grant.TenantId == actor.TenantId.Value, ct);
        if (row is null || row.EndedAt is not null || row.EndRequestedAt is not null) return row;
        row.EndRequestedAt = clock.GetUtcNow().UtcDateTime;
        await audit.RecordInTransactionAsync(db, Entry(actor, row, EndRequestedKind,
            row.ExpiresAt <= row.EndRequestedAt ? "expired" : "requested"), ct);
        await transaction.CommitAsync(ct);
        return row;
    }

    private async Task<bool> FinishEndAsync(ContentAccessGrantRow row, CancellationToken ct)
    {
        try
        {
            // A tombstone prevents activation that raced with this request from restoring the permission.
            await permissions.RevokeAsync(row.SessionKey, row.Id.ToString(CultureInfo.InvariantCulture), Utc(row.ExpiresAt), ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested) { return false; }
        await using var db = await database.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var current = await db.ContentAccessGrants.SingleAsync(grant => grant.Id == row.Id, ct);
        if (current.EndedAt is null)
        {
            var now = clock.GetUtcNow().UtcDateTime;
            current.EndedAt = now < current.ExpiresAt ? now : current.ExpiresAt;
            var actor = new Principal(current.OperatorId, TenantId.Firm(current.TenantId), Role.PLATFORM_ADMIN);
            await audit.RecordInTransactionAsync(db, Entry(actor, current, EndedKind,
                now >= current.ExpiresAt ? "expired" : "ended"), ct);
            await transaction.CommitAsync(ct);
        }
        row.EndedAt = current.EndedAt;
        return true;
    }

    private async Task<bool> ActivateAsync(ContentAccessGrantRow row, CancellationToken ct)
    {
        if (row.EndRequestedAt is not null || row.EndedAt is not null || row.ExpiresAt <= clock.GetUtcNow().UtcDateTime)
            return false;
        try
        {
            return await permissions.ActivateAsync(new ContentPermission(row.SessionKey,
                row.Id.ToString(CultureInfo.InvariantCulture), Utc(row.ExpiresAt)), ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested) { return false; }
    }

    private static bool ValidReason(string? reason) => reason is { Length: >= 2 and <= 128 }
        && reason.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or ':' or '/' or '#' or '-');

    private static void RequireOperator(Principal actor, string key)
    {
        if (!actor.IsPlatformAdmin || actor.TenantId.IsShared || string.IsNullOrWhiteSpace(actor.UserId)
            || string.IsNullOrWhiteSpace(actor.TenantId.Value))
            throw new UnauthorizedAccessException("Only a tenant-scoped operator may manage content permission.");
        if (key.Length != 64 || !key.All(char.IsAsciiHexDigit))
            throw new ArgumentException("Content permission requires a derived operator session key.", nameof(key));
    }

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    private static ContentGrantDto Dto(ContentAccessGrantRow row) => new(row.Id, row.OperatorId, row.Reason,
        Utc(row.StartedAt), Utc(row.ExpiresAt), row.EndRequestedAt is { } requested ? Utc(requested) : null,
        row.EndedAt is { } ended ? Utc(ended) : null);
    private static AuditEntry Entry(Principal actor, ContentAccessGrantRow row, string kind, string outcome) =>
        new(actor, null, null, kind, $"grantId={row.Id.ToString(CultureInfo.InvariantCulture)} reason={row.Reason}"
            + $" startedAt={Utc(row.StartedAt):O} expiresAt={Utc(row.ExpiresAt):O}"
            + (row.EndRequestedAt is { } requested ? $" endRequestedAt={Utc(requested):O}" : "")
            + (row.EndedAt is { } ended ? $" endedAt={Utc(ended):O}" : ""), outcome, 0, kind);
}

public sealed class ContentAccessGrantReconciler(ContentAccessGrants grants, ILogger<ContentAccessGrantReconciler> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await grants.ReconcileAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { logger.LogWarning("Content permission reconciliation will retry."); }
            if (!await timer.WaitForNextTickAsync(stoppingToken)) break;
        }
    }
}
