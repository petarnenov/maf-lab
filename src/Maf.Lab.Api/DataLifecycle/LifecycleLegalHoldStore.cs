using System.Data;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Plugins.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Maf.Lab.Api.DataLifecycle;

/// <summary>Durable hold admission shares the SQLite writer transaction with destructive job admission.</summary>
internal sealed class LifecycleLegalHoldStore(IDbContextFactory<MafDbContext> database, TimeProvider clock)
{
    public async Task<LifecycleLegalHoldSnapshot> CreateAsync(TenantId tenant, string name, string? recordType = null,
        CancellationToken ct = default)
    {
        ValidateTenant(tenant);
        if (string.IsNullOrWhiteSpace(name) || name.Length > 160 || name != name.Trim() ||
            name.Any(char.IsControl) || name.Any(c => c is '\u2028' or '\u2029'))
            throw new ArgumentException("A hold name must be a nonblank single line of at most 160 characters.", nameof(name));
        if (recordType is not null && (recordType.Length is < 1 or > 80 ||
            !char.IsAsciiLetterOrDigit(recordType[0]) ||
            recordType.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '.' or ':'))))
            throw new ArgumentException("A record type must be a stable identifier of at most 80 characters.", nameof(recordType));

        await using var db = await database.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (await db.LifecycleJobs.AnyAsync(j => j.TenantId == tenant.Value &&
                (j.Operation == LifecycleOperation.Retention || j.Operation == LifecycleOperation.TenantOffboard) &&
                (j.State == LifecycleJobState.Queued || j.State == LifecycleJobState.Running || j.State == LifecycleJobState.Stopping), ct))
            throw new LifecycleLegalHoldConflictException();
        var row = new LifecycleLegalHoldRow { Id = Guid.NewGuid().ToString("N"), TenantId = tenant.Value,
            Name = name, RecordType = recordType, CreatedAt = clock.GetUtcNow() };
        db.LifecycleLegalHolds.Add(row);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Snapshot(row);
    }

    public async Task<LifecycleLegalHoldSnapshot?> GetAsync(TenantId tenant, string id, CancellationToken ct = default)
    {
        ValidateTenant(tenant);
        await using var db = await database.CreateDbContextAsync(ct);
        var row = await db.LifecycleLegalHolds.AsNoTracking().SingleOrDefaultAsync(h => h.TenantId == tenant.Value && h.Id == id, ct);
        return row is null ? null : Snapshot(row);
    }

    public async Task<IReadOnlyList<LifecycleLegalHoldSnapshot>> ListActiveAsync(TenantId tenant, CancellationToken ct = default)
    {
        ValidateTenant(tenant);
        await using var db = await database.CreateDbContextAsync(ct);
        var rows = await db.LifecycleLegalHolds.AsNoTracking().Where(h => h.TenantId == tenant.Value && h.ReleasedAt == null)
            .OrderBy(h => h.Id).ToListAsync(ct);
        return rows.Select(Snapshot).ToArray();
    }

    public async Task<LifecycleLegalHoldSnapshot> ReleaseAsync(TenantId tenant, string id, CancellationToken ct = default)
    {
        ValidateTenant(tenant);
        await using var db = await database.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var row = await db.LifecycleLegalHolds.SingleOrDefaultAsync(h => h.TenantId == tenant.Value && h.Id == id, ct)
            ?? throw new KeyNotFoundException("The legal hold was not found in this tenant.");
        row.ReleasedAt ??= clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Snapshot(row);
    }

    internal static LifecycleLegalHoldSnapshot Snapshot(LifecycleLegalHoldRow row) =>
        new(row.Id, TenantId.Firm(row.TenantId), row.Name, row.RecordType, row.CreatedAt, row.ReleasedAt);
    private static void ValidateTenant(TenantId tenant) => _ = new DataLifecycleScope(tenant);
}
