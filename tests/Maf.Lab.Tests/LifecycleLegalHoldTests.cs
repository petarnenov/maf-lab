using System.Data.Common;
using Maf.Lab.Api.DataLifecycle;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Tests;

public sealed class LifecycleLegalHoldTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static TenantId A => TenantId.Firm("firm-a");
    private static TenantId B => TenantId.Firm("firm-b");

    [Theory]
    [InlineData((int)LifecycleOperation.Retention, null)]
    [InlineData((int)LifecycleOperation.Retention, "label")]
    [InlineData((int)LifecycleOperation.TenantOffboard, null)]
    [InlineData((int)LifecycleOperation.TenantOffboard, "plugin:document")]
    public async Task Active_named_hold_refuses_destructive_admission_and_release_unblocks(int operationValue, string? recordType)
    {
        var operation = (LifecycleOperation)operationValue;
        await using var f = await Fixture.CreateAsync();
        var hold = await f.Holds.CreateAsync(A, "Customer litigation", recordType, Ct);
        var failure = await Assert.ThrowsAsync<LifecycleLegalHoldBlockedException>(() =>
            f.Jobs.EnqueueAsync(new(A), operation, ["core"], Cutoff(operation), Ct));
        Assert.Contains(hold.Name, failure.Message, StringComparison.Ordinal);
        Assert.Equal(hold.Id, Assert.Single(failure.Holds).Id);
        await using (var check = f.CreateDbContext()) Assert.Empty(await check.LifecycleJobs.ToListAsync(Ct));
        var released = await f.Holds.ReleaseAsync(A, hold.Id, Ct);
        Assert.NotNull(released.ReleasedAt);
        Assert.Equal(released, await f.Holds.ReleaseAsync(A, hold.Id, Ct));
        Assert.Empty(await f.Holds.ListActiveAsync(A, Ct));
        Assert.Equal(LifecycleJobState.Queued,
            (await f.Jobs.EnqueueAsync(new(A), operation, ["core"], Cutoff(operation), Ct)).State);
    }

    [Theory]
    [InlineData((int)LifecycleOperation.Retention)]
    [InlineData((int)LifecycleOperation.TenantOffboard)]
    public async Task Hold_blocks_resume_without_changing_checkpoint_or_attempt(int operationValue)
    {
        var operation = (LifecycleOperation)operationValue;
        await using var f = await Fixture.CreateAsync();
        var job = await f.Jobs.EnqueueAsync(new(A), operation, ["core", "plugin"], Cutoff(operation), Ct);
        var attempt = await f.Jobs.ClaimAsync(A, job.Id, Ct);
        await f.Jobs.CheckpointAsync(A, job.Id, attempt.AttemptId!, "core", Ct);
        var stopped = await f.Jobs.FailAsync(A, job.Id, attempt.AttemptId!, Ct);
        var hold = await f.Holds.CreateAsync(A, "Discovery preservation", "label", Ct);
        await Assert.ThrowsAsync<LifecycleLegalHoldBlockedException>(() => f.Jobs.ResumeAsync(A, job.Id, Ct));
        var unchanged = await f.Jobs.GetAsync(A, job.Id, Ct);
        Assert.NotNull(unchanged);
        Assert.Equal(stopped.State, unchanged.State);
        Assert.Equal(stopped.CompletedParticipants, unchanged.CompletedParticipants);
        Assert.Equal(stopped.AttemptId, unchanged.AttemptId);
        await f.Holds.ReleaseAsync(A, hold.Id, Ct);
        var resumed = await f.Jobs.ResumeAsync(A, job.Id, Ct);
        Assert.Equal(1, resumed.CompletedParticipants);
        Assert.Equal(LifecycleJobState.Queued, resumed.State);
    }

    [Theory]
    [InlineData((int)LifecycleOperation.Retention)]
    [InlineData((int)LifecycleOperation.TenantOffboard)]
    public async Task Hold_creation_waits_for_actual_stop_acknowledgment(int operationValue)
    {
        var operation = (LifecycleOperation)operationValue;
        await using var f = await Fixture.CreateAsync();
        var job = await f.Jobs.EnqueueAsync(new(A), operation, ["core"], Cutoff(operation), Ct);
        await Assert.ThrowsAsync<LifecycleLegalHoldConflictException>(() => f.Holds.CreateAsync(A, "Preserve", ct: Ct));
        var claimed = await f.Jobs.ClaimAsync(A, job.Id, Ct);
        await Assert.ThrowsAsync<LifecycleLegalHoldConflictException>(() => f.Holds.CreateAsync(A, "Preserve", ct: Ct));
        await f.Jobs.RequestStopAsync(A, job.Id, Ct);
        await Assert.ThrowsAsync<LifecycleLegalHoldConflictException>(() => f.Holds.CreateAsync(A, "Preserve", ct: Ct));
        Assert.Empty(await f.Holds.ListActiveAsync(A, Ct));
        Assert.Equal(LifecycleJobState.Stopping, (await f.Jobs.GetAsync(A, job.Id, Ct))!.State);
        await f.Jobs.AcknowledgeStoppedAsync(A, job.Id, claimed.AttemptId!, Ct);
        Assert.NotNull(await f.Holds.CreateAsync(A, "Preserve", ct: Ct));
    }

    [Theory]
    [InlineData((int)LifecycleOperation.TenantExport)]
    [InlineData((int)LifecycleOperation.UserDeletion)]
    public async Task Export_and_user_deletion_can_run_under_existing_or_new_holds(int operationValue)
    {
        var operation = (LifecycleOperation)operationValue;
        await using var f = await Fixture.CreateAsync();
        await f.Holds.CreateAsync(A, "Existing hold", ct: Ct);
        var job = await f.Jobs.EnqueueAsync(new(A, operation == LifecycleOperation.UserDeletion ? "adam" : null),
            operation, ["core"], ct: Ct);
        var attempt = await f.Jobs.ClaimAsync(A, job.Id, Ct);
        await f.Holds.CreateAsync(A, "New hold", "label", Ct);
        await f.Jobs.FailAsync(A, job.Id, attempt.AttemptId!, Ct);
        Assert.Equal(LifecycleJobState.Queued, (await f.Jobs.ResumeAsync(A, job.Id, Ct)).State);
        Assert.Equal(2, (await f.Holds.ListActiveAsync(A, Ct)).Count);
    }

    [Fact]
    public async Task Hold_reads_release_and_job_admission_are_tenant_scoped_and_durable()
    {
        await using var f = await Fixture.CreateAsync();
        var hold = await f.Holds.CreateAsync(A, "Preserve customer records", "conversation", Ct);
        var reloaded = new LifecycleLegalHoldStore(f, TimeProvider.System);
        Assert.Equal(hold, await reloaded.GetAsync(A, hold.Id, Ct));
        Assert.Equal(hold, Assert.Single(await reloaded.ListActiveAsync(A, Ct)));
        Assert.Null(await reloaded.GetAsync(B, hold.Id, Ct));
        Assert.Empty(await reloaded.ListActiveAsync(B, Ct));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => reloaded.ReleaseAsync(B, hold.Id, Ct));
        Assert.Null((await reloaded.GetAsync(A, hold.Id, Ct))!.ReleasedAt);
        var foreignJob = await f.Jobs.EnqueueAsync(new(B), LifecycleOperation.TenantOffboard, ["core"], ct: Ct);
        // Neither the foreign active job nor the foreign hold influences this tenant's admission.
        Assert.NotNull(await reloaded.CreateAsync(A, "Second hold", ct: Ct));
        await Assert.ThrowsAsync<LifecycleLegalHoldBlockedException>(() =>
            f.Jobs.EnqueueAsync(new(A), LifecycleOperation.TenantOffboard, ["core"], ct: Ct));
        Assert.Equal(LifecycleJobState.Queued, (await f.Jobs.GetAsync(B, foreignJob.Id, Ct))!.State);
    }

    [Fact]
    public async Task Invalid_tenant_names_and_record_types_do_not_persist_holds()
    {
        await using var f = await Fixture.CreateAsync();
        foreach (var name in new[] { "", " ", " hold", "hold ", "a\nb", "a\tb", "a\0b", "a\u2028b", new string('a', 161) })
            await Assert.ThrowsAsync<ArgumentException>(() => f.Holds.CreateAsync(A, name, ct: Ct));
        foreach (var type in new[] { "", " ", " message", "a\nb", "a/b", "-label", new string('a', 81) })
            await Assert.ThrowsAsync<ArgumentException>(() => f.Holds.CreateAsync(A, "Valid name", type, Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => f.Holds.CreateAsync(default, "Valid name", ct: Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => f.Holds.CreateAsync(TenantId.Shared, "Valid name", ct: Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => f.Holds.GetAsync(TenantId.Shared, "id", Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => f.Holds.ListActiveAsync(default, Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => f.Holds.ReleaseAsync(default, "id", Ct));
        await using var check = f.CreateDbContext();
        Assert.Empty(await check.LifecycleLegalHolds.ToListAsync(Ct));
    }

    [Fact]
    public async Task Cancellation_cannot_create_or_release_a_hold()
    {
        await using var f = await Fixture.CreateAsync();
        var hold = await f.Holds.CreateAsync(A, "Preserve", ct: Ct);
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Holds.CreateAsync(A, "Canceled", ct: canceled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Holds.ReleaseAsync(A, hold.Id, canceled.Token));
        var unchanged = Assert.Single(await f.Holds.ListActiveAsync(A, Ct));
        Assert.Equal(hold, unchanged);
    }

    [Fact]
    public async Task Cancellation_after_SQL_mutation_rolls_back_create_and_release()
    {
        using var cancellation = new CancellationTokenSource();
        var interceptor = new CancelAfterSave(cancellation);
        await using var f = await Fixture.CreateAsync(interceptor);
        var hold = await f.Holds.CreateAsync(A, "Preserve", ct: Ct);
        interceptor.Armed = true;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            f.Holds.CreateAsync(A, "Canceled after insert", ct: cancellation.Token));
        Assert.True(interceptor.Fired);
        Assert.Equal(hold, Assert.Single(await f.Holds.ListActiveAsync(A, Ct)));

        using var releaseCancellation = new CancellationTokenSource();
        interceptor.Cancellation = releaseCancellation;
        interceptor.Armed = true;
        interceptor.Fired = false;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            f.Holds.ReleaseAsync(A, hold.Id, releaseCancellation.Token));
        Assert.True(interceptor.Fired);
        Assert.Equal(hold, await f.Holds.GetAsync(A, hold.Id, Ct));
    }

    [Fact]
    public async Task Legacy_retention_obeys_holds_and_continues_other_tenants_then_purges_after_release()
    {
        await using var f = await Fixture.CreateAsync();
        var old = DateTime.UtcNow.AddDays(-120);
        await using (var db = f.CreateDbContext())
        {
            db.Conversations.AddRange(
                new ConversationRow { Id = "a-old", TenantId = A.Value, UserId = "adam", CreatedAt = old, LastActivityAt = old },
                new ConversationRow { Id = "b-old", TenantId = B.Value, UserId = "bianca", CreatedAt = old, LastActivityAt = old });
            db.Messages.AddRange(
                new MessageRow { ConversationId = "a-old", Role = "user", Text = "held content" },
                new MessageRow { ConversationId = "b-old", Role = "user", Text = "expired content" });
            await db.SaveChangesAsync(Ct);
        }
        var hold = await f.Holds.CreateAsync(A, "Litigation", "conversation", Ct);
        var now = DateTimeOffset.UtcNow;
        using var retention = new MessageRetentionService(f, new CoreDataLifecycle(f),
            Options.Create(new MessageRetentionOptions()), new FixedClock(now), NullLogger<MessageRetentionService>.Instance);
        Assert.Equal(1, await retention.PurgeAsync(Ct));
        await using (var check = f.CreateDbContext())
        {
            Assert.Equal("a-old", Assert.Single(await check.Conversations.ToListAsync(Ct)).Id);
            Assert.Equal("held content", Assert.Single(await check.Messages.ToListAsync(Ct)).Text);
            var job = Assert.Single(await check.LifecycleJobs.ToListAsync(Ct));
            Assert.Equal(B.Value, job.TenantId);
            Assert.Equal(LifecycleJobState.Succeeded, job.State);
            Assert.Equal(now.AddDays(-90), job.RetainFrom);
        }
        await f.Holds.ReleaseAsync(A, hold.Id, Ct);
        Assert.Equal(1, await retention.PurgeAsync(Ct));
        Assert.Equal(0, await retention.PurgeAsync(Ct));
        await using var empty = f.CreateDbContext();
        Assert.Empty(await empty.Conversations.ToListAsync(Ct));
        Assert.Empty(await empty.Messages.ToListAsync(Ct));
    }

    [Fact]
    public async Task Legacy_retention_skips_busy_tenant_without_blocking_other_tenants()
    {
        await using var f = await Fixture.CreateAsync();
        var old = DateTime.UtcNow.AddDays(-120);
        await using (var db = f.CreateDbContext())
        {
            db.Conversations.AddRange(
                new ConversationRow { Id = "a-old", TenantId = A.Value, UserId = "adam", CreatedAt = old, LastActivityAt = old },
                new ConversationRow { Id = "b-old", TenantId = B.Value, UserId = "bianca", CreatedAt = old, LastActivityAt = old });
            await db.SaveChangesAsync(Ct);
        }
        var occupying = await f.Jobs.EnqueueAsync(new(A), LifecycleOperation.TenantExport, ["core"], ct: Ct);
        using var retention = new MessageRetentionService(f, new CoreDataLifecycle(f),
            Options.Create(new MessageRetentionOptions()), TimeProvider.System, NullLogger<MessageRetentionService>.Instance);
        Assert.Equal(1, await retention.PurgeAsync(Ct));
        await using (var db = f.CreateDbContext())
            Assert.Equal("a-old", Assert.Single(await db.Conversations.ToListAsync(Ct)).Id);
        Assert.Equal(LifecycleJobState.Queued, (await f.Jobs.GetAsync(A, occupying.Id, Ct))!.State);
        await f.Jobs.RequestStopAsync(A, occupying.Id, Ct);
        Assert.Equal(1, await retention.PurgeAsync(Ct));
        await using var check = f.CreateDbContext();
        Assert.Empty(await check.Conversations.ToListAsync(Ct));
    }

    [Fact]
    public async Task Cancellation_after_durable_enqueue_stops_unclaimed_retention_and_next_sweep_can_run()
    {
        using var cancellation = new CancellationTokenSource();
        var interceptor = new AfterQueuedCommit(() => cancellation.Cancel());
        await using var f = await Fixture.CreateAsync(interceptor);
        await SeedOldConversationAsync(f);
        using var retention = new MessageRetentionService(f, new CoreDataLifecycle(f),
            Options.Create(new MessageRetentionOptions()), TimeProvider.System, NullLogger<MessageRetentionService>.Instance);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => retention.PurgeAsync(cancellation.Token));
        Assert.True(interceptor.Fired);
        await AssertStoppedUnclaimedAsync(f);
        var hold = await f.Holds.CreateAsync(A, "Preserve after canceled admission", ct: Ct);
        await f.Holds.ReleaseAsync(A, hold.Id, Ct);
        Assert.Equal(1, await retention.PurgeAsync(Ct));
    }

    [Fact]
    public async Task Transient_claim_failure_stops_unclaimed_retention_without_hiding_original_error()
    {
        FailNextFactory? factory = null;
        var interceptor = new AfterQueuedCommit(() => factory!.FailNext = true);
        await using var f = await Fixture.CreateAsync(interceptor);
        factory = new FailNextFactory(f);
        await SeedOldConversationAsync(f);
        using var retention = new MessageRetentionService(factory, new CoreDataLifecycle(factory),
            Options.Create(new MessageRetentionOptions()), TimeProvider.System, NullLogger<MessageRetentionService>.Instance);
        var failure = await Assert.ThrowsAsync<IOException>(() => retention.PurgeAsync(Ct));
        Assert.Equal("Transient fixture claim failure", failure.Message);
        Assert.True(interceptor.Fired);
        await AssertStoppedUnclaimedAsync(f);
        var hold = await f.Holds.CreateAsync(A, "Preserve after failed claim", ct: Ct);
        await f.Holds.ReleaseAsync(A, hold.Id, Ct);
        Assert.Equal(1, await retention.PurgeAsync(Ct));
    }

    private static async Task SeedOldConversationAsync(Fixture f)
    {
        await using var db = f.CreateDbContext();
        var old = DateTime.UtcNow.AddDays(-120);
        db.Conversations.Add(new() { Id = "a-old", TenantId = A.Value, UserId = "adam", CreatedAt = old, LastActivityAt = old });
        await db.SaveChangesAsync(Ct);
    }

    private static async Task AssertStoppedUnclaimedAsync(Fixture f)
    {
        await using var db = f.CreateDbContext();
        var job = Assert.Single(await db.LifecycleJobs.ToListAsync(Ct));
        Assert.Equal(LifecycleJobState.Stopped, job.State);
        Assert.Null(job.AttemptId);
        Assert.Equal(0, job.CompletedParticipants);
        Assert.Equal("a-old", Assert.Single(await db.Conversations.ToListAsync(Ct)).Id);
    }

    [Fact]
    public async Task Claim_rechecks_persisted_hold_without_changing_queued_job()
    {
        await using var f = await Fixture.CreateAsync();
        var job = await f.Jobs.EnqueueAsync(new(A), LifecycleOperation.TenantOffboard, ["core"], ct: Ct);
        // Defense against an imported/restored hold bypassing normal admission.
        await using (var db = f.CreateDbContext())
        {
            db.LifecycleLegalHolds.Add(new() { Id = "restored", TenantId = A.Value, Name = "Restored preservation",
                CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(Ct);
        }
        await Assert.ThrowsAsync<LifecycleLegalHoldBlockedException>(() => f.Jobs.ClaimAsync(A, job.Id, Ct));
        var unchanged = await f.Jobs.GetAsync(A, job.Id, Ct);
        Assert.Equal(LifecycleJobState.Queued, unchanged!.State);
        Assert.Null(unchanged.AttemptId);
    }

    [Theory]
    [InlineData((int)LifecycleOperation.Retention)]
    [InlineData((int)LifecycleOperation.TenantOffboard)]
    public async Task Independent_replicas_admit_exactly_one_of_hold_or_destructive_job(int operationValue)
    {
        var operation = (LifecycleOperation)operationValue;
        var path = Path.Combine(Path.GetTempPath(), $"maf-lifecycle-hold-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<MafDbContext>().UseSqlite($"Data Source={path};Pooling=False")
                .AddInterceptors(new SqlitePragmaInterceptor()).Options;
            await using (var db = new MafDbContext(options)) await db.Database.EnsureCreatedAsync(Ct);
            var factory = new FileFactory(options);
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<bool> AdmitHold()
            {
                await start.Task;
                try { await new LifecycleLegalHoldStore(factory, TimeProvider.System).CreateAsync(A, "Race hold", ct: Ct); return true; }
                catch (LifecycleLegalHoldConflictException) { return false; }
            }
            async Task<bool> AdmitJob()
            {
                await start.Task;
                try { await new LifecycleJobStore(factory).EnqueueAsync(new(A), operation, ["core"], Cutoff(operation), Ct); return true; }
                catch (LifecycleLegalHoldBlockedException) { return false; }
            }
            var holdTask = Task.Run(AdmitHold, Ct);
            var jobTask = Task.Run(AdmitJob, Ct);
            start.SetResult();
            Assert.Single(await Task.WhenAll(holdTask, jobTask), result => result);
            await using var check = new MafDbContext(options);
            Assert.Equal(1, await check.LifecycleJobs.CountAsync(Ct) + await check.LifecycleLegalHolds.CountAsync(Ct));
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + "-wal");
            File.Delete(path + "-shm");
        }
    }

    private static DateTimeOffset? Cutoff(LifecycleOperation operation) =>
        operation == LifecycleOperation.Retention ? DateTimeOffset.UtcNow.AddDays(-90) : null;
    private sealed class FileFactory(DbContextOptions<MafDbContext> options) : IDbContextFactory<MafDbContext>
    {
        public MafDbContext CreateDbContext() => new(options);
    }
    private sealed class Fixture(SqliteConnection connection, DbContextOptions<MafDbContext> options)
        : IDbContextFactory<MafDbContext>, IAsyncDisposable
    {
        public LifecycleLegalHoldStore Holds => new(this, TimeProvider.System);
        public LifecycleJobStore Jobs => new(this);
        public MafDbContext CreateDbContext() => new(options);
        public async ValueTask DisposeAsync() => await connection.DisposeAsync();
        public static async Task<Fixture> CreateAsync(IInterceptor? interceptor = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync(Ct);
            var builder = new DbContextOptionsBuilder<MafDbContext>().UseSqlite(connection);
            if (interceptor is not null) builder.AddInterceptors(interceptor);
            var f = new Fixture(connection, builder.Options);
            await using var db = f.CreateDbContext();
            await db.Database.EnsureCreatedAsync(Ct);
            return f;
        }
    }

    private sealed class CancelAfterSave(CancellationTokenSource cancellation) : SaveChangesInterceptor
    {
        public CancellationTokenSource Cancellation { get; set; } = cancellation;
        public bool Armed { get; set; }
        public bool Fired { get; set; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (Armed)
            {
                Armed = false;
                Fired = true;
                Cancellation.Cancel();
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class AfterQueuedCommit(Action onCommit) : DbTransactionInterceptor
    {
        public bool Fired { get; private set; }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (!Fired && eventData.Context?.ChangeTracker.Entries<LifecycleJobRow>()
                    .Any(e => e.Entity.State == LifecycleJobState.Queued) == true)
            {
                Fired = true;
                onCommit();
            }
            return Task.CompletedTask;
        }
    }

    private sealed class FailNextFactory(IDbContextFactory<MafDbContext> inner) : IDbContextFactory<MafDbContext>
    {
        public bool FailNext { get; set; }
        public MafDbContext CreateDbContext()
        {
            if (FailNext)
            {
                FailNext = false;
                throw new IOException("Transient fixture claim failure");
            }
            return inner.CreateDbContext();
        }
        public Task<MafDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CreateDbContext());
        }
    }
}
