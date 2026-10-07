using Maf.Lab.Api.Admin;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Admin;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Plugins.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Maf.Lab.Tests;

/// <summary>State that must survive being served by different api replicas (two instances, one database).</summary>
public class ReplicaStateTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Job_started_on_one_replica_is_visible_and_deduplicated_on_another()
    {
        var db = await NewDatabaseAsync();
        var a = Runner(db, "replica-a");
        var b = Runner(db, "replica-b");
        var release = new TaskCompletionSource();

        var started = await a.StartAsync("firm-a", "index", async _ => { await release.Task; return "indexed 3"; }, Ct);
        Assert.Equal(AdminJobStates.Running, started.State);

        var duplicate = await b.StartAsync("firm-a", "index", _ => Task.FromResult("should not run"), Ct);
        Assert.Equal(started.JobId, duplicate.JobId);
        Assert.Equal(started.JobId, (await b.CurrentAsync("firm-a", Ct))!.JobId);
        Assert.Equal(AdminJobStates.Running, (await b.GetAsync("firm-a", started.JobId, Ct))!.State);

        var otherFirm = await b.StartAsync("firm-b", "index", _ => Task.FromResult("b done"), Ct);
        Assert.NotEqual(started.JobId, otherFirm.JobId);
        Assert.Null(await b.GetAsync("firm-b", started.JobId, Ct));

        release.SetResult();
        var finished = await WaitAsync(b, "firm-a", started.JobId);
        Assert.Equal(AdminJobStates.Succeeded, finished.State);
        Assert.Equal("indexed 3", finished.Summary);

        var next = await b.StartAsync("firm-a", "index", _ => Task.FromResult("again"), Ct);
        Assert.NotEqual(started.JobId, next.JobId);
    }

    [Fact]
    public async Task Job_of_a_dead_replica_is_reported_interrupted_and_no_longer_blocks()
    {
        var db = await NewDatabaseAsync();
        await using (var ctx = await db.CreateDbContextAsync(Ct))
        {
            ctx.AdminJobs.Add(new AdminJobRow
            {
                Id = "j_dead", TenantId = "firm-a", Kind = "migrate", State = AdminJobStates.Running, OwnerInstance = "replica-gone",
                StartedAt = DateTime.UtcNow.AddMinutes(-10), HeartbeatAt = DateTime.UtcNow.AddMinutes(-5),
            });
            await ctx.SaveChangesAsync(Ct);
        }
        var runner = Runner(db, "replica-b");

        var dead = await runner.GetAsync("firm-a", "j_dead", Ct);
        Assert.Equal(AdminJobStates.Failed, dead!.State);
        Assert.Equal(AdminJobRunner.Interrupted, dead.Summary);

        var fresh = await runner.StartAsync("firm-a", "migrate", _ => Task.FromResult("migrated"), Ct);
        Assert.NotEqual("j_dead", fresh.JobId);
        Assert.Equal(AdminJobStates.Succeeded, (await WaitAsync(runner, "firm-a", fresh.JobId)).State);
    }

    [Fact]
    public async Task A_job_the_service_stops_during_is_reported_interrupted()
    {
        var db = await NewDatabaseAsync();
        var lifetime = new ApplicationLifetime(NullLogger<ApplicationLifetime>.Instance);
        var runner = Runner(db, "replica-a", lifetime: lifetime);
        var job = await runner.StartAsync("firm-a", "coverage.refresh", async ct => { await Task.Delay(Timeout.Infinite, ct); return "never"; }, Ct);

        lifetime.StopApplication();

        var ended = await WaitAsync(runner, "firm-a", job.JobId);
        Assert.Equal((AdminJobStates.Failed, AdminJobRunner.Interrupted), (ended.State, ended.Summary));
    }

    [Fact]
    public async Task A_job_that_names_its_reason_says_it_and_any_other_failure_does_not()
    {
        var db = await NewDatabaseAsync();
        var runner = Runner(db, "replica-a");

        var named = await runner.StartAsync("firm-a", "a", _ => throw new AdminJobFailure("The coverage runner could not be reached."), Ct);
        Assert.Equal("The coverage runner could not be reached.", (await WaitAsync(runner, "firm-a", named.JobId)).Summary);
        var other = await runner.StartAsync("firm-b", "b", _ => throw new InvalidOperationException("at /app-data/secret.db"), Ct);
        Assert.Equal("The job failed; see server logs.", (await WaitAsync(runner, "firm-b", other.JobId)).Summary);
    }

    [Fact]
    public async Task A_job_cancelled_through_the_other_replica_stops_and_says_how_far_it_got()
    {
        var db = await NewDatabaseAsync();
        var a = Runner(db, "replica-a");
        var b = Runner(db, "replica-b");
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var job = await a.StartAsync("firm-a", "index", async (progress, ct) =>
        {
            progress.Report("indexed 3 of 10 documents");
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException)
            {
                stopped.TrySetResult();
                throw;
            }
            return "never";
        }, Ct);

        var (outcome, canceling) = await b.CancelAsync("firm-a", job.JobId, Ct);

        Assert.Equal(AdminJobCancel.Canceling, outcome);
        Assert.Equal(AdminJobStates.Canceled, canceling!.State);
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        var ended = await WaitForSummaryAsync(b, "firm-a", job.JobId, "It had indexed 3 of 10 documents");
        Assert.Equal(AdminJobStates.Canceled, ended.State);
        // It no longer holds the one-running-job lock.
        var next = await b.StartAsync("firm-a", "index", _ => Task.FromResult("again"), Ct);
        Assert.NotEqual(job.JobId, next.JobId);
    }

    [Fact]
    public async Task A_late_end_does_not_undo_a_cancel()
    {
        var db = await NewDatabaseAsync();
        var a = Runner(db, "replica-a");
        var release = new TaskCompletionSource();
        // Work that does not look at its token: it finishes after the cancel, and its end must not count.
        var job = await a.StartAsync("firm-a", "migrate", async _ => { await release.Task; return "migrated everything"; }, Ct);

        await a.CancelAsync("firm-a", job.JobId, Ct);
        release.SetResult();
        await Task.Delay(300, Ct);

        var after = await a.GetAsync("firm-a", job.JobId, Ct);
        Assert.Equal((AdminJobStates.Canceled, AdminJobRunner.CanceledSummary), (after!.State, after.Summary));
    }

    [Fact]
    public async Task Cancelling_an_ended_or_unknown_job_says_so()
    {
        var db = await NewDatabaseAsync();
        var runner = Runner(db, "replica-a");
        var done = await runner.StartAsync("firm-a", "index", _ => Task.FromResult("indexed 1"), Ct);
        await WaitAsync(runner, "firm-a", done.JobId);

        Assert.Equal(AdminJobCancel.AlreadyEnded, (await runner.CancelAsync("firm-a", done.JobId, Ct)).Outcome);
        Assert.Equal(AdminJobStates.Succeeded, (await runner.GetAsync("firm-a", done.JobId, Ct))!.State);
        Assert.Equal(AdminJobCancel.NotFound, (await runner.CancelAsync("firm-a", "j_nope", Ct)).Outcome);
        // Another firm's job is not this admin's to see, let alone stop.
        Assert.Equal(AdminJobCancel.NotFound, (await runner.CancelAsync("firm-b", done.JobId, Ct)).Outcome);
    }

    [Theory]
    [InlineData(AdminJobStates.Canceled, true)]
    [InlineData(AdminJobStates.Running, false)]
    [InlineData(AdminJobStates.Succeeded, false)]
    public async Task The_job_watch_fires_only_on_a_recorded_cancel(string state, bool fires)
    {
        var db = await NewDatabaseAsync();
        await using (var ctx = await db.CreateDbContextAsync(Ct))
        {
            ctx.AdminJobs.Add(new AdminJobRow
            {
                Id = "j_w", TenantId = "firm-a", Kind = "index", State = state, OwnerInstance = "replica-a",
                StartedAt = DateTime.UtcNow, HeartbeatAt = DateTime.UtcNow,
            });
            await ctx.SaveChangesAsync(Ct);
        }

        var time = new FakeTimeProvider();
        var every = TimeSpan.FromMilliseconds(20);
        var reads = new CountingFactory(db);
        await using var watch = JobCancelWatch.Start(reads, "j_w", every, time);
        time.Advance(every);
        await UntilAsync(() => reads.Reads >= 1);
        // A second read starts only once the first one's state has been looked at; a cancel ends the watch instead.
        time.Advance(every);
        await UntilAsync(() => reads.Reads >= 2 || watch.Canceled);

        Assert.Equal(fires, watch.Canceled);
    }

    [Fact]
    public async Task A_disposed_job_watch_stops_reading()
    {
        var db = await NewDatabaseAsync();
        var watch = JobCancelWatch.Start(db, "j_none", TimeSpan.FromMilliseconds(20), TimeProvider.System);

        // Disposing waits for the loop to end: nothing is left polling the database afterwards.
        await watch.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2), Ct);

        Assert.False(watch.Canceled);
    }

    [Fact]
    public async Task Running_job_keeps_its_heartbeat_fresh()
    {
        var db = await NewDatabaseAsync();
        var time = new FakeTimeProvider();
        var heartbeat = TimeSpan.FromMilliseconds(50);
        var runner = Runner(db, "replica-a", heartbeat: heartbeat, staleAfter: TimeSpan.FromMilliseconds(400), time: time);
        var running = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var job = await runner.StartAsync("firm-a", "index", async _ => { running.SetResult(); await release.Task; return "ok"; }, Ct);
        // The work starts after the heartbeat's timer exists, so no tick below is lost.
        await running.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        // Past the stale cutoff, one beat at a time, each written before the clock moves on.
        for (var beat = 0; beat < 10; beat++)
        {
            time.Advance(heartbeat);
            var now = time.GetUtcNow().UtcDateTime;
            await UntilAsync(async () =>
            {
                await using var ctx = await db.CreateDbContextAsync(Ct);
                return (await ctx.AdminJobs.AsNoTracking().SingleAsync(j => j.Id == job.JobId, Ct)).HeartbeatAt >= now;
            });
        }
        Assert.Equal(AdminJobStates.Running, (await runner.GetAsync("firm-a", job.JobId, Ct))!.State);
        release.SetResult();
        Assert.Equal(AdminJobStates.Succeeded, (await WaitAsync(runner, "firm-a", job.JobId)).State);
    }

    [Fact]
    public async Task Initializer_is_idempotent_concurrent_and_adds_new_tables_to_an_old_database()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("maf-db-").FullName, "maf.db");
        var factory = Factory(path);
        await using (var ctx = await factory.CreateDbContextAsync(Ct))
        {
            // An "old" database: everything except the AdminJobs table.
            foreach (var statement in DatabaseInitializer.Statements(ctx.Database.GenerateCreateScript()).Where(s => !s.Contains("AdminJobs")))
            {
                await ctx.Database.ExecuteSqlRawAsync(statement, Ct);
            }
        }

        await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var ctx = await factory.CreateDbContextAsync(Ct);
            await DatabaseInitializer.InitializeAsync(ctx, Ct);
        }));

        await using var check = await factory.CreateDbContextAsync(Ct);
        Assert.Equal(0, await check.AdminJobs.CountAsync(Ct));
        var mode = await check.Database.SqlQueryRaw<string>("PRAGMA journal_mode").ToListAsync(Ct);
        Assert.Equal("wal", mode.Single());
    }

    [Fact]
    public async Task Initializer_drops_the_traces_table_of_before_the_monitor_became_a_plugin()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("maf-db-drop-").FullName, "maf.db");
        var factory = Factory(path);
        await using (var ctx = await factory.CreateDbContextAsync(Ct))
        {
            // An old database: it still has the full traces table, with content.
            await ctx.Database.ExecuteSqlRawAsync(
                "CREATE TABLE \"TurnTraces\" (\"TurnId\" TEXT PRIMARY KEY, \"Json\" TEXT NOT NULL); INSERT INTO \"TurnTraces\" VALUES ('t', '[]');", Ct);
        }

        await Task.WhenAll(Enumerable.Range(0, 3).Select(async _ =>
        {
            await using var ctx = await factory.CreateDbContextAsync(Ct);
            await DatabaseInitializer.InitializeAsync(ctx, Ct);
        }));

        await using var check = await factory.CreateDbContextAsync(Ct);
        Assert.Empty(await ColumnsAsync(check, "TurnTraces"));
    }

    [Fact]
    public async Task Initializer_adds_a_new_column_to_a_table_an_old_database_already_has()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("maf-db-col-").FullName, "maf.db");
        var factory = Factory(path);
        await using (var ctx = await factory.CreateDbContextAsync(Ct))
        {
            // An "old" database: Turns exists, but without the column a turn's reasoning goes in.
            foreach (var statement in DatabaseInitializer.Statements(ctx.Database.GenerateCreateScript()))
            {
                await ctx.Database.ExecuteSqlRawAsync(WithoutColumn(statement, "Reasoning"), Ct);
            }
            Assert.DoesNotContain("Reasoning", await ColumnsAsync(ctx, "Turns"));
        }

        await using (var ctx = await factory.CreateDbContextAsync(Ct))
        {
            await DatabaseInitializer.InitializeAsync(ctx, Ct);
            ctx.Turns.Add(new TurnRow
            {
                Id = "t_1",
                ConversationId = "c",
                UserId = "adam",
                TenantId = "firm-a",
                Question = "q",
                Reasoning = "thought",
                CreatedAt = DateTime.UtcNow,
            });
            await ctx.SaveChangesAsync(Ct);
        }

        await using var check = await factory.CreateDbContextAsync(Ct);
        Assert.Contains("Reasoning", await ColumnsAsync(check, "Turns"));
        Assert.Equal("thought", (await check.Turns.SingleAsync(t => t.Id == "t_1", Ct)).Reasoning);
    }

    /// <summary>The same CREATE TABLE as the model's, minus one column.</summary>
    private static string WithoutColumn(string statement, string column)
    {
        var lines = statement.Split('\n').ToList();
        var at = lines.FindIndex(l => l.TrimStart().StartsWith($"\"{column}\" ", StringComparison.Ordinal));
        if (at < 0)
        {
            return statement;
        }
        var removed = lines[at].TrimEnd('\r', ' ');
        lines.RemoveAt(at);
        // The last column has no comma: the one before it then must lose its own.
        if (!removed.EndsWith(',') && at > 0)
        {
            var before = lines[at - 1].TrimEnd('\r', ' ');
            if (before.EndsWith(',')) { lines[at - 1] = before[..^1]; }
        }
        return string.Join('\n', lines);
    }

    private static async Task<List<string>> ColumnsAsync(MafDbContext ctx, string table)
    {
        await ctx.Database.OpenConnectionAsync(Ct);
        await using var command = ctx.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table}\")";
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var names = new List<string>();
        while (await reader.ReadAsync(Ct))
        {
            names.Add(reader.GetString(1));
        }
        return names;
    }

    [Fact]
    public async Task Two_api_hosts_on_one_database_write_concurrently_and_continue_each_others_conversations()
    {
        var dir = Directory.CreateTempSubdirectory("maf-replicas-").FullName;
        using var hostA = new ApiFactory(ApiFactory.ProceduralModel("Answer from A."), dataDir: dir);
        using var hostB = new ApiFactory(ApiFactory.ProceduralModel("Answer from B."), dataDir: dir);
        var clientA = hostA.ClientFor("adam", "firm-a", Role.USER);
        var clientB = hostB.ClientFor("adam", "firm-a", Role.USER);

        // Concurrent turns from both replicas into the same file.
        var turns = await Task.WhenAll(Enumerable.Range(0, 6).Select(i =>
            ApiFactory.ChatAsync(i % 2 == 0 ? clientA : clientB, $"explain proration case {i}")));
        Assert.All(turns, events => Assert.False(events[^1].Data.TryGetProperty("error", out var e) && e.ValueKind == System.Text.Json.JsonValueKind.String));

        // Turn 1 on A, turn 2 on B: B sees A's history.
        var first = await ApiFactory.ChatAsync(clientA, "explain FS-REQUIRED");
        var conversationId = ApiFactory.ThreadOf(first);
        var before = hostB.Chat.Requests.Count;
        await ApiFactory.ChatAsync(clientB, "and what about proration?", conversationId);
        var history = hostB.Chat.Requests[before].Messages.Select(m => m.Text).ToList();
        Assert.Contains("explain FS-REQUIRED", history);
        Assert.Contains("Answer from A.", history);

        await using var ctx = ChatApiTests.Db(hostA);
        Assert.Equal(8, await ctx.Turns.CountAsync(Ct));
    }

    private static async Task<IDbContextFactory<MafDbContext>> NewDatabaseAsync()
    {
        var factory = Factory(Path.Combine(Directory.CreateTempSubdirectory("maf-jobs-").FullName, "maf.db"));
        await using var ctx = await factory.CreateDbContextAsync(Ct);
        await DatabaseInitializer.InitializeAsync(ctx, Ct);
        return factory;
    }

    private static IDbContextFactory<MafDbContext> Factory(string path) => new PooledDbContextFactory<MafDbContext>(
        new DbContextOptionsBuilder<MafDbContext>().UseSqlite($"Data Source={path}").AddInterceptors(new SqlitePragmaInterceptor()).Options);

    private static AdminJobRunner Runner(IDbContextFactory<MafDbContext> db, string instance, TimeSpan? heartbeat = null, TimeSpan? staleAfter = null,
        ApplicationLifetime? lifetime = null, TimeProvider? time = null) =>
        new(db, Options.Create(new AdminJobOptions
        {
            HeartbeatInterval = heartbeat ?? TimeSpan.FromSeconds(10), StaleAfter = staleAfter ?? TimeSpan.FromSeconds(60),
            CancelPollEvery = TimeSpan.FromMilliseconds(20),
        }),
            NullLogger<AdminJobRunner>.Instance, time ?? TimeProvider.System, lifetime ?? new ApplicationLifetime(NullLogger<ApplicationLifetime>.Instance))
        { Instance = instance };

    private static async Task<AdminJob> WaitForSummaryAsync(AdminJobRunner runner, string firm, string jobId, string part)
    {
        for (var i = 0; i < 100; i++)
        {
            var job = await runner.GetAsync(firm, jobId, Ct);
            if (job!.Summary?.Contains(part, StringComparison.Ordinal) == true)
            {
                return job;
            }
            await Task.Delay(50, Ct);
        }
        throw new TimeoutException(jobId);
    }

    /// <summary>Waits for what a watch or a job does on its own thread; the bound only keeps a broken one from hanging the run.</summary>
    private static Task UntilAsync(Func<bool> done) => UntilAsync(() => Task.FromResult(done()));

    private static async Task UntilAsync(Func<Task<bool>> done)
    {
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        bound.CancelAfter(TimeSpan.FromSeconds(10));
        while (!await done())
        {
            await Task.Delay(5, bound.Token);
        }
    }

    /// <summary>Counts the contexts a watch opens: one per read of the job's row.</summary>
    private sealed class CountingFactory(IDbContextFactory<MafDbContext> inner) : IDbContextFactory<MafDbContext>
    {
        private int reads;
        public int Reads => Volatile.Read(ref reads);

        public MafDbContext CreateDbContext()
        {
            Interlocked.Increment(ref reads);
            return inner.CreateDbContext();
        }

        public Task<MafDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref reads);
            return inner.CreateDbContextAsync(cancellationToken);
        }
    }

    private static async Task<AdminJob> WaitAsync(AdminJobRunner runner, string firm, string jobId)
    {
        for (var i = 0; i < 100; i++)
        {
            var job = await runner.GetAsync(firm, jobId, Ct);
            if (job!.State is AdminJobStates.Succeeded or AdminJobStates.Failed)
            {
                return job;
            }
            await Task.Delay(50, Ct);
        }
        throw new TimeoutException(jobId);
    }
}
