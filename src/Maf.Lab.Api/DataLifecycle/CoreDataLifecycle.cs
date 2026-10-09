using System.Runtime.CompilerServices;
using System.Text.Json;
using Maf.Lab.Api.Storage;
using Maf.Lab.Plugins.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Maf.Lab.Api.DataLifecycle;

/// <summary>Core content participant. The job must check legal holds and quiesce writers before invoking it.</summary>
public sealed class CoreDataLifecycle(IDbContextFactory<MafDbContext> factory) : IDataLifecycle, IContributesDataLifecycle
{
    public IDataLifecycle CreateDataLifecycle(IServiceProvider services) => this;

    public async IAsyncEnumerable<DataExportRecord> ExportAsync(DataLifecycleScope scope,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ct.ThrowIfCancellationRequested();
        await using var db = await factory.CreateDbContextAsync(ct);
        await EnsureUserOwnershipAsync(db, scope, ct);
        var selection = new Selection(db, scope);
        await foreach (var row in Records("conversation", selection.Conversations, ct)) yield return row;
        await foreach (var row in Records("message", selection.Messages, ct)) yield return row;
        await foreach (var row in Records("turn", selection.Turns, ct)) yield return row;
        await foreach (var row in Records("feedback", selection.Feedback, ct)) yield return row;
        await foreach (var row in Records("label", selection.Labels, ct)) yield return row;
        await foreach (var row in Records("pending-write", selection.PendingWrites, ct)) yield return row;
    }

    public Task DeleteAsync(DataLifecycleScope scope, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return DeleteContentAsync(scope, null, ct);
    }

    public Task ApplyRetentionAsync(DataRetentionPolicy policy, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return PurgeAsync(policy, ct);
    }

    internal Task<int> PurgeAsync(DataRetentionPolicy policy, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return DeleteContentAsync(policy.Scope, policy.RetainFrom.UtcDateTime, ct);
    }

    private static async Task EnsureUserOwnershipAsync(MafDbContext db, DataLifecycleScope scope, CancellationToken ct)
    {
        if (scope.UserId is { } user && await db.Labels.AnyAsync(l => l.TenantId == scope.Tenant.Value
                && l.ReviewerId != user && !db.Turns.Any(t => t.TenantId == scope.Tenant.Value && t.Id == l.TurnId), ct))
            throw new InvalidOperationException("User lifecycle requires ownership repair: a retained label has no owning turn.");
    }

    private async Task<int> DeleteContentAsync(DataLifecycleScope scope, DateTime? cutoff, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await EnsureUserOwnershipAsync(db, scope, ct);
        var selection = new Selection(db, scope, cutoff);
        // A departing reviewer can have labels on another user's surviving turn. Recompute only those flags.
        await db.Turns.Where(t => t.TenantId == scope.Tenant.Value && !selection.Turns.Any(x => x.Id == t.Id)
                && selection.Labels.Any(l => l.TurnId == t.Id))
            .ExecuteUpdateAsync(update => update.SetProperty(t => t.Labeled,
                t => db.Labels.Any(l => l.TenantId == scope.Tenant.Value && l.TurnId == t.Id
                    && !selection.Labels.Any(selected => selected.Id == l.Id))), ct);
        await selection.PendingWrites.ExecuteDeleteAsync(ct);
        await selection.Labels.ExecuteDeleteAsync(ct);
        await selection.Feedback.ExecuteDeleteAsync(ct);
        await selection.Turns.ExecuteDeleteAsync(ct);
        await selection.Messages.ExecuteDeleteAsync(ct);
        var deleted = await selection.Conversations.ExecuteDeleteAsync(ct);
        await transaction.CommitAsync(ct);
        return deleted;
    }

    private static async IAsyncEnumerable<DataExportRecord> Records<T>(string type, IQueryable<T> query,
        [EnumeratorCancellation] CancellationToken ct) where T : class
    {
        await foreach (var row in query.AsNoTracking().AsAsyncEnumerable().WithCancellation(ct))
        {
            ct.ThrowIfCancellationRequested();
            yield return new DataExportRecord(type, JsonSerializer.SerializeToElement(row));
        }
    }

    private sealed class Selection
    {
        public IQueryable<ConversationRow> Conversations { get; }
        public IQueryable<MessageRow> Messages { get; }
        public IQueryable<TurnRow> Turns { get; }
        public IQueryable<FeedbackRow> Feedback { get; }
        public IQueryable<LabelRow> Labels { get; }
        public IQueryable<PendingWriteRow> PendingWrites { get; }

        public Selection(MafDbContext db, DataLifecycleScope scope, DateTime? cutoff = null)
        {
            var tenant = scope.Tenant.Value;
            var user = scope.UserId;
            var retention = cutoff.HasValue;
            Conversations = db.Conversations.Where(c => c.TenantId == tenant && (user == null || c.UserId == user));
            if (cutoff is { } before) Conversations = Conversations.Where(c => c.LastActivityAt < before);
            var conversationIds = Conversations.Select(c => c.Id);
            Messages = db.Messages.Where(m => conversationIds.Contains(m.ConversationId));
            Turns = db.Turns.Where(t => t.TenantId == tenant &&
                (conversationIds.Contains(t.ConversationId) || (!retention && (user == null || t.UserId == user))));
            var turnIds = Turns.Select(t => t.Id);
            Feedback = db.Feedback.Where(f => f.TenantId == tenant &&
                (conversationIds.Contains(f.ConversationId) || turnIds.Contains(f.TurnId)
                    || (!retention && (user == null || f.UserId == user))));
            Labels = db.Labels.Where(l => l.TenantId == tenant &&
                (turnIds.Contains(l.TurnId) || (!retention && (user == null || l.ReviewerId == user))));
            PendingWrites = db.PendingWrites.Where(p => p.TenantId == tenant &&
                (conversationIds.Contains(p.ConversationId) || turnIds.Contains(p.TurnId)
                    || (!retention && (user == null || p.UserId == user))));
        }
    }
}
