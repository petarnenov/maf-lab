using System.Data;
using System.Text.Json;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Plugins.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Maf.Lab.Api.DataLifecycle;

/// <summary>
/// Internal orchestration journal, not authorization or writer quiescence. The caller must drain all participant work
/// before acknowledging a stop/failure/success. A lost worker keeps its slot: elapsed time cannot prove it stopped.
/// </summary>
internal sealed class LifecycleJobStore(IDbContextFactory<MafDbContext> database)
{
    public async Task<LifecycleJobSnapshot> EnqueueAsync(DataLifecycleScope scope, LifecycleOperation operation,
        IReadOnlyList<string> participants, DateTimeOffset? retainFrom = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(participants);
        if (!Enum.IsDefined(operation) || (operation == LifecycleOperation.UserDeletion) != (scope.UserId is not null)
            || (operation == LifecycleOperation.Retention) != retainFrom.HasValue)
            throw new ArgumentException("The lifecycle operation requires its exact user/cutoff scope.");
        var plan = participants.ToArray();
        if (plan.Length == 0 || plan.Any(string.IsNullOrWhiteSpace) || plan.Distinct(StringComparer.Ordinal).Count() != plan.Length)
            throw new ArgumentException("A lifecycle plan requires unique nonblank participant identifiers.", nameof(participants));
        await using var db = await database.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await RequireNoLegalHoldsAsync(db, scope.Tenant, operation, ct);
        await RequireFreeSlotAsync(db, scope.Tenant, ct);
        var row = new LifecycleJobRow { Id = Guid.NewGuid().ToString("N"), TenantId = scope.Tenant.Value,
            UserId = scope.UserId, Operation = operation, RetainFrom = retainFrom, ParticipantsJson = JsonSerializer.Serialize(plan),
            State = LifecycleJobState.Queued, ExportGeneration = 1 };
        db.LifecycleJobs.Add(row);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Snapshot(row);
    }

    public async Task<LifecycleJobSnapshot?> GetAsync(TenantId tenant, string id, CancellationToken ct = default)
    {
        ValidateTenant(tenant);
        await using var db = await database.CreateDbContextAsync(ct);
        var row = await db.LifecycleJobs.AsNoTracking().SingleOrDefaultAsync(j => j.TenantId == tenant.Value && j.Id == id, ct);
        return row is null ? null : Snapshot(row);
    }

    public Task<LifecycleJobSnapshot> ClaimAsync(TenantId tenant, string id, CancellationToken ct = default) =>
        MutateAsync(tenant, id, row =>
        {
            RequireState(row, LifecycleJobState.Queued);
            row.State = LifecycleJobState.Running;
            row.AttemptId = Guid.NewGuid().ToString("N");
        }, ct, requireHoldAdmission: true);

    public Task<LifecycleJobSnapshot> RequestStopAsync(TenantId tenant, string id, CancellationToken ct = default) =>
        MutateAsync(tenant, id, row =>
        {
            if (row.State == LifecycleJobState.Queued) row.State = LifecycleJobState.Stopped;
            else if (row.State == LifecycleJobState.Running) row.State = LifecycleJobState.Stopping;
            else if (row.State is not (LifecycleJobState.Stopping or LifecycleJobState.Stopped))
                throw new InvalidOperationException("Only an active lifecycle job can be stopped.");
        }, ct);

    public Task<LifecycleJobSnapshot> CheckpointAsync(TenantId tenant, string id, string attemptId, string participant,
        CancellationToken ct = default) => MutateAsync(tenant, id, row =>
        {
            RequireAttempt(row, attemptId);
            var plan = Plan(row);
            if (row.CompletedParticipants >= plan.Length || plan[row.CompletedParticipants] != participant)
                throw new InvalidOperationException("Only the next completed participant can be checkpointed.");
            row.CompletedParticipants++;
        }, ct);

    /// <summary>Call only after the current participant and its dependent work have unwound.</summary>
    public Task<LifecycleJobSnapshot> AcknowledgeStoppedAsync(TenantId tenant, string id, string attemptId,
        CancellationToken ct = default) => MutateAsync(tenant, id, row =>
        {
            RequireAttempt(row, attemptId);
            row.State = LifecycleJobState.Stopped;
        }, ct);

    /// <summary>Call only after all work owned by the attempt has unwound.</summary>
    public Task<LifecycleJobSnapshot> FailAsync(TenantId tenant, string id, string attemptId, CancellationToken ct = default) =>
        MutateAsync(tenant, id, row =>
        {
            RequireAttempt(row, attemptId);
            row.State = row.State == LifecycleJobState.Stopping ? LifecycleJobState.Stopped : LifecycleJobState.Failed;
        }, ct);

    public Task<LifecycleJobSnapshot> SucceedAsync(TenantId tenant, string id, string attemptId, CancellationToken ct = default) =>
        MutateAsync(tenant, id, row =>
        {
            RequireAttempt(row, attemptId);
            if (row.State == LifecycleJobState.Stopping)
            {
                row.State = LifecycleJobState.Stopped;
                return;
            }
            if (row.CompletedParticipants != Plan(row).Length)
                throw new InvalidOperationException("Every participant must complete before the job succeeds.");
            row.State = LifecycleJobState.Succeeded;
        }, ct);

    public async Task<LifecycleJobSnapshot> ResumeAsync(TenantId tenant, string id, CancellationToken ct = default)
    {
        ValidateTenant(tenant);
        await using var db = await database.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var row = await FindAsync(db, tenant, id, ct);
        if (row.State is not (LifecycleJobState.Stopped or LifecycleJobState.Failed))
            throw new InvalidOperationException("Only a drained stopped or failed job can resume.");
        await RequireNoLegalHoldsAsync(db, tenant, row.Operation, ct);
        await RequireFreeSlotAsync(db, tenant, ct);
        row.State = LifecycleJobState.Queued;
        row.AttemptId = null;
        if (row.Operation == LifecycleOperation.TenantExport)
        {
            // Private export staging belongs to this generation. The runner discards it; it is never a completed bundle.
            row.CompletedParticipants = 0;
            row.ExportGeneration = checked(row.ExportGeneration + 1);
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Snapshot(row);
    }

    private async Task<LifecycleJobSnapshot> MutateAsync(TenantId tenant, string id, Action<LifecycleJobRow> change,
        CancellationToken ct, bool requireHoldAdmission = false)
    {
        ValidateTenant(tenant);
        await using var db = await database.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var row = await FindAsync(db, tenant, id, ct);
        if (requireHoldAdmission)
            await RequireNoLegalHoldsAsync(db, tenant, row.Operation, ct);
        change(row);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Snapshot(row);
    }

    private static async Task<LifecycleJobRow> FindAsync(MafDbContext db, TenantId tenant, string id, CancellationToken ct) =>
        await db.LifecycleJobs.SingleOrDefaultAsync(j => j.TenantId == tenant.Value && j.Id == id, ct)
        ?? throw new KeyNotFoundException("The lifecycle job was not found in this tenant.");

    private static async Task RequireFreeSlotAsync(MafDbContext db, TenantId tenant, CancellationToken ct)
    {
        if (await db.LifecycleJobs.AnyAsync(j => j.TenantId == tenant.Value &&
            (j.State == LifecycleJobState.Queued || j.State == LifecycleJobState.Running || j.State == LifecycleJobState.Stopping), ct))
            throw new LifecycleJobBusyException();
    }

    private static async Task RequireNoLegalHoldsAsync(MafDbContext db, TenantId tenant,
        LifecycleOperation operation, CancellationToken ct)
    {
        if (operation is not (LifecycleOperation.Retention or LifecycleOperation.TenantOffboard)) return;
        // These operations cover the whole tenant, including every record family. A family hold
        // therefore blocks admission just as a tenant hold does; no protected step may run first.
        var holds = await db.LifecycleLegalHolds.AsNoTracking()
            .Where(h => h.TenantId == tenant.Value && h.ReleasedAt == null)
            .OrderBy(h => h.Id).ToListAsync(ct);
        if (holds.Count > 0)
            throw new LifecycleLegalHoldBlockedException(holds.Select(h => new LifecycleLegalHoldSnapshot(
                h.Id, tenant, h.Name, h.RecordType, h.CreatedAt, h.ReleasedAt)).ToArray());
    }

    private static void ValidateTenant(TenantId tenant) => _ = new DataLifecycleScope(tenant);
    private static void RequireState(LifecycleJobRow row, LifecycleJobState state)
    {
        if (row.State != state) throw new InvalidOperationException("The lifecycle job is not in the required state.");
    }
    private static void RequireAttempt(LifecycleJobRow row, string attempt)
    {
        if (string.IsNullOrWhiteSpace(attempt) || row.AttemptId != attempt ||
            row.State is not (LifecycleJobState.Running or LifecycleJobState.Stopping))
            throw new InvalidOperationException("The lifecycle attempt no longer owns this job.");
    }
    private static string[] Plan(LifecycleJobRow row) => JsonSerializer.Deserialize<string[]>(row.ParticipantsJson)!;
    private static LifecycleJobSnapshot Snapshot(LifecycleJobRow row) => new(row.Id, TenantId.Firm(row.TenantId), row.UserId,
        row.Operation, row.RetainFrom, Array.AsReadOnly(Plan(row)), row.CompletedParticipants, row.State, row.AttemptId, row.ExportGeneration);
}

internal sealed class LifecycleJobBusyException() : InvalidOperationException("Another lifecycle job still owns this tenant.");

/// <summary>Named holds are returned to the authorized caller, never included in the retention worker's logs.</summary>
internal sealed class LifecycleLegalHoldBlockedException : InvalidOperationException
{
    public IReadOnlyList<LifecycleLegalHoldSnapshot> Holds { get; }

    public LifecycleLegalHoldBlockedException(IReadOnlyList<LifecycleLegalHoldSnapshot> holds)
        : base($"Lifecycle deletion is blocked by legal hold(s): {string.Join(", ", holds.Select(h => h.Name))}.")
    {
        Holds = Array.AsReadOnly(holds.ToArray());
    }
}
