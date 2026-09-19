using Maf.Lab.Api.Admin;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Admin;
using Maf.Lab.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

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
                Id = "j_dead", FirmId = "firm-a", Kind = "migrate", State = AdminJobStates.Running, OwnerInstance = "replica-gone",
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
    public async Task Running_job_keeps_its_heartbeat_fresh()
    {
        var db = await NewDatabaseAsync();
        var runner = Runner(db, "replica-a", heartbeat: TimeSpan.FromMilliseconds(50), staleAfter: TimeSpan.FromMilliseconds(400));
        var release = new TaskCompletionSource();
        var job = await runner.StartAsync("firm-a", "index", async _ => { await release.Task; return "ok"; }, Ct);

        await Task.Delay(1000, Ct);
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
    public async Task Two_api_hosts_on_one_database_write_concurrently_and_continue_each_others_conversations()
    {
        var dir = Directory.CreateTempSubdirectory("maf-replicas-").FullName;
        using var hostA = new ApiFactory(ApiFactory.ProceduralModel("Answer from A."), dataDir: dir);
        using var hostB = new ApiFactory(ApiFactory.ProceduralModel("Answer from B."), dataDir: dir);
        var clientA = hostA.ClientFor("adam", "firm-a", Role.ADVISOR);
        var clientB = hostB.ClientFor("adam", "firm-a", Role.ADVISOR);

        // Concurrent turns from both replicas into the same file.
        var turns = await Task.WhenAll(Enumerable.Range(0, 6).Select(i =>
            ApiFactory.ChatAsync(i % 2 == 0 ? clientA : clientB, $"explain proration case {i}")));
        Assert.All(turns, events => Assert.False(events[^1].Data.TryGetProperty("error", out var e) && e.ValueKind == System.Text.Json.JsonValueKind.String));

        // Turn 1 on A, turn 2 on B: B sees A's history.
        var first = await ApiFactory.ChatAsync(clientA, "explain FS-REQUIRED");
        var conversationId = first[^1].Data.GetProperty("conversationId").GetString();
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

    private static AdminJobRunner Runner(IDbContextFactory<MafDbContext> db, string instance, TimeSpan? heartbeat = null, TimeSpan? staleAfter = null) =>
        new(db, Options.Create(new AdminJobOptions { HeartbeatInterval = heartbeat ?? TimeSpan.FromSeconds(10), StaleAfter = staleAfter ?? TimeSpan.FromSeconds(60) }),
            NullLogger<AdminJobRunner>.Instance, TimeProvider.System, new ApplicationLifetime(NullLogger<ApplicationLifetime>.Instance))
        { Instance = instance };

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
