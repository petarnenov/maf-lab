using Maf.Lab.Api.DataLifecycle;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Plugins.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Maf.Lab.Tests;

public sealed class LifecycleJobRunnerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly TenantId Tenant = TenantId.Firm("firm-a");
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static Task Wait(Task task) => task.WaitAsync(TimeSpan.FromSeconds(15), Ct);

    [Fact]
    public async Task Stop_waits_for_participant_cleanup_and_resume_skips_persisted_core_deletion()
    {
        await using var fixture = await Fixture.CreateAsync();
        var scope = new DataLifecycleScope(Tenant, "adam");
        var job = await fixture.Jobs.EnqueueAsync(scope, LifecycleOperation.UserDeletion, ["core", "plugin"], ct: Ct);
        var entered = Signal();
        var canceled = Signal();
        var release = Signal();
        var coreCalls = 0;
        var running = fixture.Runner.RunAsync(Tenant, job.Id, async (_, participant, token) =>
        {
            if (participant == "core")
            {
                coreCalls++;
                await new CoreDataLifecycle(fixture).DeleteAsync(scope, token);
                return;
            }
            using var registration = token.Register(() => canceled.TrySetResult());
            entered.TrySetResult();
            await Wait(release.Task); // Simulate a store draining after cancellation.
            token.ThrowIfCancellationRequested();
        }, Ct);
        try
        {
            await Wait(entered.Task);
            await fixture.Jobs.RequestStopAsync(Tenant, job.Id, Ct);
            await Wait(canceled.Task);
            Assert.False(running.IsCompleted);
            var stopping = await fixture.Jobs.GetAsync(Tenant, job.Id, Ct);
            Assert.Equal(LifecycleJobState.Stopping, stopping!.State);
            Assert.Equal(1, stopping.CompletedParticipants);
            await Assert.ThrowsAsync<LifecycleJobBusyException>(() => fixture.Jobs.EnqueueAsync(new(Tenant),
                LifecycleOperation.Retention, ["core"], DateTimeOffset.UtcNow, Ct));
        }
        finally { release.TrySetResult(); }
        Assert.Equal(LifecycleJobState.Stopped, (await running).State);
        await fixture.Jobs.ResumeAsync(Tenant, job.Id, Ct);
        var replayed = new List<string>();
        var complete = await fixture.Runner.RunAsync(Tenant, job.Id, (_, participant, _) =>
        {
            replayed.Add(participant);
            return Task.CompletedTask;
        }, Ct);
        Assert.Equal(LifecycleJobState.Succeeded, complete.State);
        Assert.Equal(["plugin"], replayed);
        Assert.Equal(1, coreCalls);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(["other-tenant", "other-user"], await db.Conversations.OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync(Ct));
    }

    [Fact]
    public async Task Stop_observed_at_checkpoint_prevents_next_step_without_waiting_for_poll()
    {
        await using var fixture = await Fixture.CreateAsync();
        var job = await fixture.Jobs.EnqueueAsync(new(Tenant), LifecycleOperation.TenantOffboard, ["first", "second"], ct: Ct);
        var runner = new LifecycleJobRunner(fixture.Jobs, TimeProvider.System, TimeSpan.FromHours(1));
        var calls = new List<string>();
        var stopped = await runner.RunAsync(Tenant, job.Id, async (_, participant, _) =>
        {
            calls.Add(participant);
            await fixture.Jobs.RequestStopAsync(Tenant, job.Id, Ct);
        }, Ct);
        Assert.Equal(["first"], calls);
        Assert.Equal(LifecycleJobState.Stopped, stopped.State);
        Assert.Equal(1, stopped.CompletedParticipants);
    }

    [Fact]
    public async Task Failed_step_is_replayed_without_repeating_completed_steps()
    {
        await using var fixture = await Fixture.CreateAsync();
        var job = await fixture.Jobs.EnqueueAsync(new(Tenant), LifecycleOperation.TenantOffboard, ["first", "second"], ct: Ct);
        var failed = await fixture.Runner.RunAsync(Tenant, job.Id, (_, participant, _) =>
            participant == "second" ? Task.FromException(new InvalidOperationException("private data")) : Task.CompletedTask, Ct);
        Assert.Equal(LifecycleJobState.Failed, failed.State);
        Assert.Equal(1, failed.CompletedParticipants);
        await fixture.Jobs.ResumeAsync(Tenant, job.Id, Ct);
        var calls = new List<string>();
        var complete = await fixture.Runner.RunAsync(Tenant, job.Id, (_, participant, _) =>
        {
            calls.Add(participant);
            return Task.CompletedTask;
        }, Ct);
        Assert.Equal(["second"], calls);
        Assert.Equal(LifecycleJobState.Succeeded, complete.State);
    }

    [Fact]
    public async Task Host_cancellation_drains_the_executor_before_acknowledging_stop()
    {
        await using var fixture = await Fixture.CreateAsync();
        var job = await fixture.Jobs.EnqueueAsync(new(Tenant), LifecycleOperation.TenantExport, ["core"], ct: Ct);
        using var host = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var entered = Signal();
        var release = Signal();
        var running = fixture.Runner.RunAsync(Tenant, job.Id, async (_, _, token) =>
        {
            entered.TrySetResult();
            await Wait(release.Task);
            token.ThrowIfCancellationRequested();
        }, host.Token);
        try
        {
            await Wait(entered.Task);
            await host.CancelAsync();
            Assert.False(running.IsCompleted);
            Assert.Equal(LifecycleJobState.Running, (await fixture.Jobs.GetAsync(Tenant, job.Id, Ct))!.State);
        }
        finally { release.TrySetResult(); }
        Assert.Equal(LifecycleJobState.Stopped, (await running).State);
    }

    [Fact]
    public async Task Failure_to_read_stop_state_cancels_work_and_does_not_advance_to_next_store()
    {
        await using var fixture = await Fixture.CreateAsync();
        var job = await fixture.Jobs.EnqueueAsync(new(Tenant), LifecycleOperation.TenantExport, ["first", "second"], ct: Ct);
        var entered = Signal();
        var canceled = Signal();
        var release = Signal();
        var calls = new List<string>();
        var running = fixture.Runner.RunAsync(Tenant, job.Id, async (_, participant, token) =>
        {
            calls.Add(participant);
            using var registration = token.Register(() => canceled.TrySetResult());
            entered.TrySetResult();
            await Wait(release.Task);
            token.ThrowIfCancellationRequested();
        }, Ct);
        try
        {
            await Wait(entered.Task);
            fixture.FailReads = true;
            await Wait(canceled.Task);
            Assert.False(running.IsCompleted);
        }
        finally
        {
            fixture.FailReads = false;
            release.TrySetResult();
        }
        Assert.Equal(LifecycleJobState.Failed, (await running).State);
        Assert.Equal(["first"], calls);
    }

    [Fact]
    public async Task Export_resume_replays_all_stores_in_a_new_generation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var job = await fixture.Jobs.EnqueueAsync(new(Tenant), LifecycleOperation.TenantExport, ["first", "second"], ct: Ct);
        await fixture.Runner.RunAsync(Tenant, job.Id, (_, participant, _) =>
            participant == "second" ? Task.FromException(new IOException()) : Task.CompletedTask, Ct);
        await fixture.Jobs.ResumeAsync(Tenant, job.Id, Ct);
        var calls = new List<string>();
        var generations = new List<int>();
        var complete = await fixture.Runner.RunAsync(Tenant, job.Id, (snapshot, participant, _) =>
        {
            calls.Add(participant);
            generations.Add(snapshot.ExportGeneration);
            return Task.CompletedTask;
        }, Ct);
        Assert.Equal(["first", "second"], calls);
        Assert.All(generations, generation => Assert.Equal(2, generation));
        Assert.Equal(LifecycleJobState.Succeeded, complete.State);
    }

    private sealed class Fixture(string path, DbContextOptions<MafDbContext> options) : IDbContextFactory<MafDbContext>, IAsyncDisposable
    {
        public volatile bool FailReads;
        public LifecycleJobStore Jobs => new(this);
        public LifecycleJobRunner Runner => new(Jobs, TimeProvider.System, TimeSpan.FromMilliseconds(10));
        public MafDbContext CreateDbContext() => FailReads ? throw new IOException("fixture unavailable") : new(options);
        public Task<MafDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CreateDbContext());
        }
        public ValueTask DisposeAsync()
        {
            File.Delete(path);
            File.Delete(path + "-wal");
            File.Delete(path + "-shm");
            return ValueTask.CompletedTask;
        }
        public static async Task<Fixture> CreateAsync()
        {
            var path = Path.Combine(Path.GetTempPath(), $"lifecycle-runner-{Guid.NewGuid():N}.db");
            var options = new DbContextOptionsBuilder<MafDbContext>()
                .UseSqlite(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString())
                .AddInterceptors(new SqlitePragmaInterceptor()).Options;
            var fixture = new Fixture(path, options);
            await using var db = fixture.CreateDbContext();
            await DatabaseInitializer.InitializeAsync(db, Ct);
            foreach (var (id, tenant, user) in new[] { ("target", "firm-a", "adam"), ("other-user", "firm-a", "bianca"), ("other-tenant", "firm-b", "adam") })
                db.Conversations.Add(new() { Id = id, TenantId = tenant, UserId = user });
            await db.SaveChangesAsync(Ct);
            return fixture;
        }
    }
}
