using Maf.Lab.Api.DataLifecycle;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Plugins.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Maf.Lab.Tests;

public sealed class LifecycleJobStoreTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static TenantId A => TenantId.Firm("firm-a");
    private static TenantId B => TenantId.Firm("firm-b");

    [Fact]
    public async Task Stop_holds_slot_until_acknowledged_and_resume_preserves_ordered_progress()
    {
        await using var f = await Fixture.CreateAsync();
        var job = await f.Store.EnqueueAsync(new(A, "adam"), LifecycleOperation.UserDeletion, ["core", "plugin"], ct: Ct);
        var first = await f.Store.ClaimAsync(A, job.Id, Ct);
        await f.Store.CheckpointAsync(A, job.Id, first.AttemptId!, "core", Ct);
        var stopping = await f.Store.RequestStopAsync(A, job.Id, Ct);
        Assert.Equal(LifecycleJobState.Stopping, stopping.State);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Store.ResumeAsync(A, job.Id, Ct));
        await Assert.ThrowsAsync<LifecycleJobBusyException>(() => f.Store.EnqueueAsync(new(A), LifecycleOperation.TenantExport, ["core"], ct: Ct));
        await f.Store.AcknowledgeStoppedAsync(A, job.Id, first.AttemptId!, Ct);
        var resumed = await f.Store.ResumeAsync(A, job.Id, Ct);
        Assert.Equal(1, resumed.CompletedParticipants);
        var second = await f.Store.ClaimAsync(A, job.Id, Ct);
        Assert.NotEqual(first.AttemptId, second.AttemptId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Store.CheckpointAsync(A, job.Id, first.AttemptId!, "plugin", Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Store.FailAsync(A, job.Id, first.AttemptId!, Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Store.SucceedAsync(A, job.Id, second.AttemptId!, Ct));
        await f.Store.CheckpointAsync(A, job.Id, second.AttemptId!, "plugin", Ct);
        Assert.Equal(LifecycleJobState.Succeeded, (await f.Store.SucceedAsync(A, job.Id, second.AttemptId!, Ct)).State);
    }

    [Fact]
    public async Task Fixed_retention_input_is_durable_and_cannot_be_replaced_on_resume()
    {
        await using var f = await Fixture.CreateAsync();
        var plan = new[] { "core", "monitor" };
        var cutoff = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(2));
        var job = await f.Store.EnqueueAsync(new(A), LifecycleOperation.Retention, plan, cutoff, Ct);
        plan[0] = "changed";
        var claimed = await f.Store.ClaimAsync(A, job.Id, Ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Store.CheckpointAsync(A, job.Id, claimed.AttemptId!, "monitor", Ct));
        await f.Store.CheckpointAsync(A, job.Id, claimed.AttemptId!, "core", Ct);
        await f.Store.FailAsync(A, job.Id, claimed.AttemptId!, Ct);
        await f.Store.ResumeAsync(A, job.Id, Ct);
        var reloaded = await new LifecycleJobStore(f).GetAsync(A, job.Id, Ct);
        Assert.NotNull(reloaded);
        Assert.Equal(cutoff, reloaded.RetainFrom);
        Assert.Equal(["core", "monitor"], reloaded.Participants);
        Assert.Equal(1, reloaded.CompletedParticipants);
        Assert.Null(reloaded.AttemptId);
    }

    [Fact]
    public async Task Partial_export_restarts_in_new_private_generation()
    {
        await using var f = await Fixture.CreateAsync();
        var job = await f.Store.EnqueueAsync(new(A), LifecycleOperation.TenantExport, ["core", "plugin"], ct: Ct);
        var claimed = await f.Store.ClaimAsync(A, job.Id, Ct);
        await f.Store.CheckpointAsync(A, job.Id, claimed.AttemptId!, "core", Ct);
        await f.Store.FailAsync(A, job.Id, claimed.AttemptId!, Ct);
        var resumed = await f.Store.ResumeAsync(A, job.Id, Ct);
        Assert.Equal(0, resumed.CompletedParticipants);
        Assert.Equal(2, resumed.ExportGeneration);
        Assert.Equal(job.Participants, resumed.Participants);
    }

    [Fact]
    public async Task Tenant_scoping_applies_to_reads_and_every_transition()
    {
        await using var f = await Fixture.CreateAsync();
        var job = await f.Store.EnqueueAsync(new(A), LifecycleOperation.TenantExport, ["core"], ct: Ct);
        var claimed = await f.Store.ClaimAsync(A, job.Id, Ct);
        Assert.Null(await f.Store.GetAsync(B, job.Id, Ct));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => f.Store.ClaimAsync(B, job.Id, Ct));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => f.Store.RequestStopAsync(B, job.Id, Ct));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => f.Store.CheckpointAsync(B, job.Id, claimed.AttemptId!, "core", Ct));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => f.Store.AcknowledgeStoppedAsync(B, job.Id, claimed.AttemptId!, Ct));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => f.Store.FailAsync(B, job.Id, claimed.AttemptId!, Ct));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => f.Store.SucceedAsync(B, job.Id, claimed.AttemptId!, Ct));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => f.Store.ResumeAsync(B, job.Id, Ct));
        Assert.Equal(LifecycleJobState.Queued, (await f.Store.EnqueueAsync(new(B), LifecycleOperation.TenantExport, ["core"], ct: Ct)).State);
        Assert.Equal(LifecycleJobState.Running, (await f.Store.GetAsync(A, job.Id, Ct))!.State);
    }

    [Fact]
    public async Task Queued_stop_releases_slot_but_cannot_resume_over_another_operation()
    {
        await using var f = await Fixture.CreateAsync();
        var job = await f.Store.EnqueueAsync(new(A), LifecycleOperation.TenantExport, ["core"], ct: Ct);
        Assert.Equal(LifecycleJobState.Stopped, (await f.Store.RequestStopAsync(A, job.Id, Ct)).State);
        await f.Store.EnqueueAsync(new(A, "adam"), LifecycleOperation.UserDeletion, ["core"], ct: Ct);
        await Assert.ThrowsAsync<LifecycleJobBusyException>(() => f.Store.ResumeAsync(A, job.Id, Ct));
    }

    [Fact]
    public async Task Stop_wins_race_with_success_and_allows_completed_inflight_checkpoint()
    {
        await using var f = await Fixture.CreateAsync();
        var job = await f.Store.EnqueueAsync(new(A), LifecycleOperation.TenantExport, ["core"], ct: Ct);
        var claimed = await f.Store.ClaimAsync(A, job.Id, Ct);
        await f.Store.RequestStopAsync(A, job.Id, Ct);
        await f.Store.CheckpointAsync(A, job.Id, claimed.AttemptId!, "core", Ct);
        Assert.Equal(LifecycleJobState.Stopped, (await f.Store.SucceedAsync(A, job.Id, claimed.AttemptId!, Ct)).State);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Store.SucceedAsync(A, job.Id, claimed.AttemptId!, Ct));
    }

    [Fact]
    public async Task Invalid_operation_scope_or_plan_is_rejected_without_creating_a_job()
    {
        await using var f = await Fixture.CreateAsync();
        foreach (var plan in new string[][] { [], ["core", "core"], [" "] })
            await Assert.ThrowsAsync<ArgumentException>(() => f.Store.EnqueueAsync(new(A), LifecycleOperation.TenantExport, plan, ct: Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => f.Store.EnqueueAsync(new(A), LifecycleOperation.UserDeletion, ["core"], ct: Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => f.Store.EnqueueAsync(new(A, "adam"), LifecycleOperation.TenantOffboard, ["core"], ct: Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => f.Store.EnqueueAsync(new(A), LifecycleOperation.Retention, ["core"], ct: Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => f.Store.EnqueueAsync(new(A), LifecycleOperation.TenantExport, ["core"], DateTimeOffset.UtcNow, Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => f.Store.GetAsync(TenantId.Shared, "id", Ct));
        await using var db = f.CreateDbContext();
        Assert.Empty(await db.LifecycleJobs.ToListAsync(Ct));
    }

    [Fact]
    public async Task Database_enforces_cross_kind_active_slot_even_outside_store_and_initializer_is_repeatable()
    {
        await using var f = await Fixture.CreateAsync();
        var job = await f.Store.EnqueueAsync(new(A), LifecycleOperation.TenantExport, ["core"], ct: Ct);
        var claimed = await f.Store.ClaimAsync(A, job.Id, Ct);
        await f.Store.RequestStopAsync(A, job.Id, Ct);
        await using var db = f.CreateDbContext();
        await DatabaseInitializer.InitializeAsync(db, Ct);
        await DatabaseInitializer.InitializeAsync(db, Ct);
        db.LifecycleJobs.Add(new() { Id = "other", TenantId = A.Value, Operation = LifecycleOperation.Retention,
            ParticipantsJson = "[\"core\"]", State = LifecycleJobState.Queued });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
        Assert.NotNull(claimed.AttemptId);
    }

    [Fact]
    public async Task Independent_replicas_cannot_admit_overlapping_jobs()
    {
        var path = Path.Combine(Path.GetTempPath(), $"maf-lifecycle-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<MafDbContext>()
                .UseSqlite($"Data Source={path};Pooling=False")
                .AddInterceptors(new SqlitePragmaInterceptor()).Options;
            await using (var db = new MafDbContext(options)) await db.Database.EnsureCreatedAsync(Ct);
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<bool> Enqueue(LifecycleOperation operation)
            {
                await start.Task;
                try
                {
                    await new LifecycleJobStore(new FileFactory(options)).EnqueueAsync(new(A), operation, ["core"], ct: Ct);
                    return true;
                }
                catch (InvalidOperationException) { return false; }
            }
            var export = Task.Run(() => Enqueue(LifecycleOperation.TenantExport), Ct);
            var offboard = Task.Run(() => Enqueue(LifecycleOperation.TenantOffboard), Ct);
            start.SetResult();
            var results = await Task.WhenAll(export, offboard);
            Assert.Single(results, result => result);
            await using var check = new MafDbContext(options);
            Assert.Single(await check.LifecycleJobs.ToListAsync(Ct));
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + "-wal");
            File.Delete(path + "-shm");
        }
    }

    private sealed class FileFactory(DbContextOptions<MafDbContext> options) : IDbContextFactory<MafDbContext>
    {
        public MafDbContext CreateDbContext() => new(options);
    }

    private sealed class Fixture(SqliteConnection connection, DbContextOptions<MafDbContext> options)
        : IDbContextFactory<MafDbContext>, IAsyncDisposable
    {
        public LifecycleJobStore Store => new(this);
        public MafDbContext CreateDbContext() => new(options);
        public async ValueTask DisposeAsync() => await connection.DisposeAsync();
        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync(Ct);
            var f = new Fixture(connection, new DbContextOptionsBuilder<MafDbContext>().UseSqlite(connection).Options);
            await using var db = f.CreateDbContext();
            await db.Database.EnsureCreatedAsync(Ct);
            return f;
        }
    }
}
