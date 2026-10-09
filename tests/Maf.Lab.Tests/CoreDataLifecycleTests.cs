using System.Data.Common;
using Maf.Lab.Api.DataLifecycle;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Plugins.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maf.Lab.Tests;

public sealed class CoreDataLifecycleTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTime Cutoff = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static DataLifecycleScope Adam => new(TenantId.Firm("firm-a"), "adam");

    [Fact]
    public void Scope_rejects_default_shared_and_blank_user()
    {
        Assert.Throws<ArgumentException>(() => new DataLifecycleScope(default));
        Assert.Throws<ArgumentException>(() => new DataLifecycleScope(TenantId.Shared));
        Assert.Throws<ArgumentException>(() => new DataLifecycleScope(TenantId.Firm("firm-a"), " "));
        Assert.Throws<ArgumentException>(() => TenantId.Firm("../firm-a"));
        Assert.Throws<ArgumentException>(() => new DataRetentionPolicy(default, DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => new DataRetentionPolicy(TenantId.Shared, DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task User_export_contains_soft_deleted_content_traces_and_review_labels_without_other_tenant()
    {
        await using var fixture = await Fixture.CreateAsync();
        var records = await Export(fixture.Store, Adam);
        Assert.Equal(["a-old", "a-recent"], Ids(records, "conversation"));
        Assert.Equal(["turn-a-old", "turn-a-recent"], Ids(records, "turn"));
        Assert.Equal(["label-a-old", "label-a-recent", "review-adam", "review-adam-remaining"], Ids(records, "label"));
        Assert.Equal(2, records.Count(r => r.RecordType == "message"));
        Assert.Equal(2, records.Count(r => r.RecordType == "pending-write"));
        Assert.Equal(2, records.Count(r => r.RecordType == "feedback"));
        var turn = records.First(r => r.RecordType == "turn").Data;
        Assert.Equal("[{\"privateTrace\":\"kept\"}]", turn.GetProperty("RecordJson").GetString());
        Assert.Contains(records, r => r.RecordType == "conversation" && r.Data.GetProperty("DeletedAt").ValueKind != System.Text.Json.JsonValueKind.Null);
        Assert.DoesNotContain(records, r => r.Data.TryGetProperty("TenantId", out var tenant) && tenant.GetString() != "firm-a");
        Assert.DoesNotContain(records, r => r.RecordType is "audit" or "content-access-grant");
    }

    [Fact]
    public async Task User_erasure_is_idempotent_preserves_other_users_and_recomputes_surviving_review_flags()
    {
        await using var fixture = await Fixture.CreateAsync();
        var otherTenantBefore = await Export(fixture.Store, new(TenantId.Firm("firm-b")));
        await fixture.Store.DeleteAsync(Adam, Ct);
        await fixture.Store.DeleteAsync(Adam, Ct);
        Assert.Empty(await Export(fixture.Store, Adam));
        Assert.Equal(otherTenantBefore, await Export(fixture.Store, new(TenantId.Firm("firm-b"))), RecordComparer.Instance);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(["a-boundary", "a-other", "b-adam", "b-other"], await db.Conversations.OrderBy(c => c.Id).Select(c => c.Id).ToArrayAsync(Ct));
        Assert.False((await db.Turns.SingleAsync(t => t.Id == "turn-a-other", Ct)).Labeled);
        Assert.True((await db.Turns.SingleAsync(t => t.Id == "turn-a-boundary", Ct)).Labeled);
        Assert.Equal(4, await db.Messages.CountAsync(Ct));
        Assert.Equal(4, await db.PendingWrites.CountAsync(Ct));
        Assert.Equal(4, await db.Feedback.CountAsync(Ct));
        Assert.Equal("untouched-hash", (await db.Audit.SingleAsync(Ct)).Hash);
    }

    [Fact]
    public async Task Ownership_covers_detached_user_rows_and_guards_each_tenant_bearing_descendant()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using (var db = fixture.CreateDbContext())
        {
            foreach (var tenant in new[] { "firm-a", "firm-b" })
            {
                db.Turns.Add(new() { Id = tenant + "-detached", ConversationId = "a-old", TenantId = tenant,
                    UserId = "adam", Question = "private" });
                db.Feedback.Add(new() { Id = tenant + "-detached", ConversationId = "missing", TurnId = "missing",
                    TenantId = tenant, UserId = "adam", Kind = "wrong" });
                db.PendingWrites.Add(new() { Id = tenant + "-detached", ConversationId = "a-old", TurnId = "turn-a-old",
                    TenantId = tenant, UserId = "adam", ToolName = "tool", State = "s", Summary = "s", Status = "pending" });
            }
            db.Turns.Add(new() { Id = "owned-orphan", ConversationId = "missing", TenantId = "firm-a", UserId = "adam", Question = "orphan" });
            await db.SaveChangesAsync(Ct);
        }
        var before = await Export(fixture.Store, Adam);
        Assert.Contains(before, r => r.RecordType == "turn" && r.Data.GetProperty("Id").GetString() == "owned-orphan");
        Assert.Contains(before, r => r.RecordType == "feedback" && r.Data.GetProperty("Id").GetString() == "firm-a-detached");
        Assert.DoesNotContain(before, r => r.Data.TryGetProperty("TenantId", out var tenant) && tenant.GetString() == "firm-b");
        var otherBefore = await Export(fixture.Store, new(TenantId.Firm("firm-b")));
        await fixture.Store.DeleteAsync(Adam, Ct);
        Assert.Empty(await Export(fixture.Store, Adam));
        Assert.Equal(otherBefore, await Export(fixture.Store, new(TenantId.Firm("firm-b"))), RecordComparer.Instance);
    }

    [Fact]
    public async Task Historical_orphan_label_blocks_user_export_and_erasure_until_ownership_is_repaired()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using (var db = fixture.CreateDbContext())
        {
            db.Labels.Add(new() { Id = "historic-orphan", TenantId = "firm-a", TurnId = "lost-turn", ReviewerId = "bianca",
                Dataset = "private", RowJson = "{\"question\":\"lost subject private data\"}" });
            await db.SaveChangesAsync(Ct);
        }
        var tenant = new DataLifecycleScope(TenantId.Firm("firm-a"));
        var before = await Export(fixture.Store, tenant);
        await using var export = fixture.Store.ExportAsync(Adam, Ct).GetAsyncEnumerator(Ct);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => { await export.MoveNextAsync(); });
        Assert.Contains("ownership repair", error.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.DeleteAsync(Adam, Ct));
        Assert.Equal(before, await Export(fixture.Store, tenant), RecordComparer.Instance);
        await fixture.Store.DeleteAsync(tenant, Ct);
        Assert.Empty(await Export(fixture.Store, tenant));
    }

    [Fact]
    public async Task Tenant_erasure_deletes_every_content_family_only_in_target_tenant()
    {
        await using var fixture = await Fixture.CreateAsync();
        var otherBefore = await Export(fixture.Store, new(TenantId.Firm("firm-b")));
        var scope = new DataLifecycleScope(TenantId.Firm("firm-a"));
        await fixture.Store.DeleteAsync(scope, Ct);
        await fixture.Store.DeleteAsync(scope, Ct);
        Assert.Empty(await Export(fixture.Store, scope));
        Assert.Equal(otherBefore, await Export(fixture.Store, new(TenantId.Firm("firm-b"))), RecordComparer.Instance);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(2, await db.Messages.CountAsync(Ct));
        Assert.Single(await db.Audit.ToListAsync(Ct));
        Assert.Equal("preserved grant", (await db.ContentAccessGrants.SingleAsync(Ct)).Reason);
    }

    [Fact]
    public async Task Retention_uses_last_activity_and_removes_whole_old_family_including_soft_deleted()
    {
        await using var fixture = await Fixture.CreateAsync();
        var policy = new DataRetentionPolicy(TenantId.Firm("firm-a"), new DateTimeOffset(Cutoff));
        var otherBefore = await Export(fixture.Store, new(TenantId.Firm("firm-b")));
        await fixture.Store.ApplyRetentionAsync(policy, Ct);
        await fixture.Store.ApplyRetentionAsync(policy, Ct);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(["a-boundary", "a-recent", "b-adam", "b-other"], await db.Conversations.OrderBy(c => c.Id).Select(c => c.Id).ToArrayAsync(Ct));
        Assert.Equal(4, await db.Messages.CountAsync(Ct));
        Assert.Equal(4, await db.Turns.CountAsync(Ct));
        Assert.Equal(4, await db.Feedback.CountAsync(Ct));
        Assert.Equal(4, await db.PendingWrites.CountAsync(Ct));
        Assert.DoesNotContain(await db.Labels.ToListAsync(Ct), l => l.TenantId == "firm-a" && l.TurnId is "turn-a-old" or "turn-a-other");
        Assert.Equal(otherBefore, await Export(fixture.Store, new(TenantId.Firm("firm-b"))), RecordComparer.Instance);
    }

    [Fact]
    public async Task Cancellation_after_first_delete_rolls_back_every_content_change()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var interceptor = new CancelAfterDelete(cts);
        await using var fixture = await Fixture.CreateAsync(interceptor);
        var scope = new DataLifecycleScope(TenantId.Firm("firm-a"));
        var before = await Export(fixture.Store, scope);
        interceptor.Armed = true;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Store.DeleteAsync(Adam, cts.Token));
        Assert.True(interceptor.Triggered);
        Assert.Equal(before, await Export(fixture.Store, scope), RecordComparer.Instance);
    }

    [Fact]
    public async Task Precancelled_export_delete_and_retention_leave_content_unchanged()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var before = await Export(fixture.Store, Adam);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var unused in fixture.Store.ExportAsync(Adam, cts.Token)) { }
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Store.DeleteAsync(Adam, cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Store.ApplyRetentionAsync(new(TenantId.Firm("firm-a"), new(Cutoff)), cts.Token));
        Assert.Equal(before, await Export(fixture.Store, Adam), RecordComparer.Instance);
    }

    private static async Task<List<DataExportRecord>> Export(CoreDataLifecycle store, DataLifecycleScope scope)
    {
        var records = new List<DataExportRecord>();
        await foreach (var record in store.ExportAsync(scope, Ct)) records.Add(record);
        return records;
    }

    private static string[] Ids(IEnumerable<DataExportRecord> records, string type) => records
        .Where(r => r.RecordType == type).Select(r => r.Data.GetProperty("Id").GetString()!).Order(StringComparer.Ordinal).ToArray();

    private sealed class RecordComparer : IEqualityComparer<DataExportRecord>
    {
        public static readonly RecordComparer Instance = new();
        public bool Equals(DataExportRecord? x, DataExportRecord? y) => x?.RecordType == y?.RecordType && x?.Data.GetRawText() == y?.Data.GetRawText();
        public int GetHashCode(DataExportRecord obj) => HashCode.Combine(obj.RecordType, obj.Data.GetRawText());
    }

    private sealed class CancelAfterDelete(CancellationTokenSource cancellation) : DbCommandInterceptor
    {
        public bool Armed { get; set; }
        public bool Triggered { get; private set; }
        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (Armed && command.CommandText.StartsWith("DELETE", StringComparison.Ordinal))
            {
                Triggered = true;
                cancellation.Cancel();
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class Fixture(SqliteConnection connection, DbContextOptions<MafDbContext> options)
        : IDbContextFactory<MafDbContext>, IAsyncDisposable
    {
        public CoreDataLifecycle Store => new(this);
        public MafDbContext CreateDbContext() => new(options);
        public async ValueTask DisposeAsync() => await connection.DisposeAsync();

        public static async Task<Fixture> CreateAsync(DbCommandInterceptor? interceptor = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync(Ct);
            var builder = new DbContextOptionsBuilder<MafDbContext>().UseSqlite(connection);
            if (interceptor is not null) builder.AddInterceptors(interceptor);
            var fixture = new Fixture(connection, builder.Options);
            await using var db = fixture.CreateDbContext();
            await db.Database.EnsureCreatedAsync(Ct);
            foreach (var (id, user, tenant, activity) in new[]
            {
                ("a-old", "adam", "firm-a", Cutoff.AddDays(-1)),
                ("a-recent", "adam", "firm-a", Cutoff.AddDays(1)),
                ("a-other", "bianca", "firm-a", Cutoff.AddDays(-1)),
                ("a-boundary", "bianca", "firm-a", Cutoff),
                ("b-adam", "adam", "firm-b", Cutoff.AddDays(-1)),
                ("b-other", "bianca", "firm-b", Cutoff.AddDays(-1))
            })
            {
                db.Conversations.Add(new() { Id = id, UserId = user, TenantId = tenant, CreatedAt = Cutoff.AddDays(-30), LastActivityAt = activity,
                    DeletedAt = id == "a-old" ? Cutoff.AddDays(-1) : null, Title = "private title" });
                db.Messages.Add(new() { ConversationId = id, Role = "user", Text = "private message", CreatedAt = activity });
                db.Turns.Add(new() { Id = "turn-" + id, ConversationId = id, UserId = user, TenantId = tenant, Question = "private question",
                    Answer = "private answer", RecordJson = "[{\"privateTrace\":\"kept\"}]", CreatedAt = activity, Labeled = true });
                db.Feedback.Add(new() { Id = "feedback-" + id, TurnId = "turn-" + id, ConversationId = id, UserId = user,
                    TenantId = tenant, Kind = "wrong", Comment = "private comment", CreatedAt = activity });
                db.PendingWrites.Add(new() { Id = "pending-" + id, TurnId = "turn-" + id, ConversationId = id, UserId = user,
                    TenantId = tenant, ToolName = "tool", State = "signed", Summary = "private", Status = "pending", CreatedAt = activity, UpdatedAt = activity });
                if (id != "a-other") db.Labels.Add(new() { Id = "label-" + id, TurnId = "turn-" + id, TenantId = tenant,
                    ReviewerId = "bianca", Dataset = "private", RowJson = "{}", CreatedAt = activity });
            }
            db.Labels.AddRange(
                new LabelRow { Id = "review-adam", TurnId = "turn-a-other", TenantId = "firm-a", ReviewerId = "adam", Dataset = "d", RowJson = "{}" },
                new LabelRow { Id = "review-adam-remaining", TurnId = "turn-a-boundary", TenantId = "firm-a", ReviewerId = "adam", Dataset = "d", RowJson = "{}" },
                // Corrupt cross-tenant reference must never let firm-a erasure select a firm-b row.
                new LabelRow { Id = "cross-tenant-label", TurnId = "turn-a-old", TenantId = "firm-b", ReviewerId = "adam", Dataset = "d", RowJson = "{}" });
            db.Audit.Add(new() { At = Cutoff, PrincipalId = "adam", TenantId = "firm-a", ConversationId = "a-old", TurnId = "turn-a-old",
                ToolName = "tool", Arguments = "id=1", Outcome = "ok", Hash = "untouched-hash", PreviousHash = "previous" });
            db.ContentAccessGrants.Add(new() { SessionKey = "session", OperatorId = "adam", TenantId = "firm-a",
                Reason = "preserved grant", StartedAt = Cutoff, ExpiresAt = Cutoff.AddMinutes(30) });
            await db.SaveChangesAsync(Ct);
            return fixture;
        }
    }
}
