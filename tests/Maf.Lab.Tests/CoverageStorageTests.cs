using Maf.Lab.Api.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Maf.Lab.Tests;

/// <summary>The coverage and run tables (add-coverage-dashboard-and-test-agent).</summary>
public sealed class CoverageStorageTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Initializing_twice_is_idempotent()
    {
        var factory = Factory();
        await using (var ctx = await factory.CreateDbContextAsync(Ct))
        {
            await DatabaseInitializer.InitializeAsync(ctx, Ct);
            ctx.CoverageThresholds.Add(new CoverageThresholdRow { Path = "src/A.cs", Pct = 90, UpdatedBy = "alice" });
            await ctx.SaveChangesAsync(Ct);
        }
        await using (var ctx = await factory.CreateDbContextAsync(Ct))
        {
            await DatabaseInitializer.InitializeAsync(ctx, Ct);
        }

        await using var check = await factory.CreateDbContextAsync(Ct);
        Assert.Equal(90, (await check.CoverageThresholds.SingleAsync(Ct)).Pct);
    }

    [Fact]
    public async Task A_second_active_run_for_the_same_file_is_refused_by_the_database()
    {
        var factory = await NewDatabaseAsync();
        await using (var ctx = await factory.CreateDbContextAsync(Ct))
        {
            ctx.TestGenRuns.Add(Run("r_1", "src/A.cs", TestGenRunState.Working));
            await ctx.SaveChangesAsync(Ct);
        }

        await using var second = await factory.CreateDbContextAsync(Ct);
        second.TestGenRuns.Add(Run("r_2", "src/A.cs", TestGenRunState.Submitted));
        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task Finished_runs_do_not_block_a_new_one()
    {
        var factory = await NewDatabaseAsync();
        await using var ctx = await factory.CreateDbContextAsync(Ct);
        ctx.TestGenRuns.Add(Run("r_1", "src/A.cs", TestGenRunState.Failed));
        ctx.TestGenRuns.Add(Run("r_2", "src/A.cs", TestGenRunState.Accepted));
        ctx.TestGenRuns.Add(Run("r_3", "src/A.cs", TestGenRunState.Working));
        ctx.TestGenRuns.Add(Run("r_4", "src/B.cs", TestGenRunState.Candidate));
        await ctx.SaveChangesAsync(Ct);

        Assert.Equal(4, await ctx.TestGenRuns.CountAsync(Ct));
    }

    [Fact]
    public async Task A_run_stored_one_attempt_short_is_corrected_from_its_activity()
    {
        var factory = await NewDatabaseAsync();
        await using (var ctx = await factory.CreateDbContextAsync(Ct))
        {
            // r_short: its budget stopped attempt 1, and it was stored at 0. r_right: already right.
            var stopped = Run("r_short", "src/A.cs", TestGenRunState.CompletedNoChange);
            var right = Run("r_right", "src/B.cs", TestGenRunState.CompletedNoChange);
            right.Attempt = 2;
            ctx.TestGenRuns.AddRange(stopped, right);
            ctx.TestGenRunActivity.AddRange(Activity("r_short", 1, 0), Activity("r_short", 2, 1),
                Activity("r_right", 1, 1), Activity("r_right", 2, 2));
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = await factory.CreateDbContextAsync(Ct))
        {
            await DatabaseInitializer.InitializeAsync(ctx, Ct);
        }

        await using var check = await factory.CreateDbContextAsync(Ct);
        Assert.Equal([("r_right", 2), ("r_short", 1)],
            (await check.TestGenRuns.OrderBy(r => r.Id).ToListAsync(Ct)).Select(r => (r.Id, r.Attempt)));
    }

    private static TestGenRunActivityRow Activity(string runId, long seq, int attempt) => new()
    {
        RunId = runId, Seq = seq, LastSeq = seq, At = DateTime.UtcNow, Attempt = attempt, Type = "phase",
    };

    internal static TestGenRunRow Run(string id, string path, string state) => new()
    {
        Id = id,
        Path = path,
        Toolchain = "dotnet",
        CommitSha = new string('a', 40),
        TargetPct = 85,
        Model = "glm-5.3:cloud",
        State = state,
        MaxAttempts = 5,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
        CreatedBy = "alice",
    };

    internal static async Task<IDbContextFactory<MafDbContext>> NewDatabaseAsync()
    {
        var factory = Factory();
        await using var ctx = await factory.CreateDbContextAsync(Ct);
        await DatabaseInitializer.InitializeAsync(ctx, Ct);
        return factory;
    }

    private static IDbContextFactory<MafDbContext> Factory() => new PooledDbContextFactory<MafDbContext>(
        new DbContextOptionsBuilder<MafDbContext>()
            .UseSqlite($"Data Source={Path.Combine(Directory.CreateTempSubdirectory("maf-cov-").FullName, "maf.db")}")
            .AddInterceptors(new SqlitePragmaInterceptor()).Options);
}
