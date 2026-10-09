using Microsoft.EntityFrameworkCore;

namespace Maf.Lab.Plugins.Coverage;

public sealed class CoveragePlugin : IMafPlugin, IContributesServices, IContributesEndpoints, IContributesModel,
    IContributesDataMigration, IContributesOpenWork
{
    public const string PluginName = "coverage";
    public string Name => PluginName;
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        using var defaults = typeof(CoveragePlugin).Assembly.GetManifestResourceStream("CoverageDefaults")!;
        configuration = new ConfigurationBuilder().AddJsonStream(defaults).AddConfiguration(configuration).Build();
        services.AddMemoryCache();
        // The Coverage screen (add-coverage-dashboard-and-test-agent): snapshots, thresholds, and the runner that measures.
        services.Configure<CoverageOptions>(configuration.GetSection(CoverageOptions.Section));
        services.Configure<CoverageRunnerOptions>(configuration.GetSection(CoverageRunnerOptions.Section));
        services.AddSingleton<IRepository, GitRepository>();
        services.AddSingleton<CoverageStore>();
        services.AddSingleton<CoverageIngestor>();
        services.AddSingleton<CoverageRefresher>();
        services.Configure<TestAgentOptions>(configuration.GetSection(TestAgentOptions.Section));
        services.AddSingleton<ModelAvailability>();
        services.AddHttpClient(TestAgentClient.HttpClientName);
        services.AddSingleton<TestAgentClient>();
        services.AddHttpClient(TestAgentProbe.HttpClientName);
        services.AddSingleton<TestAgentProbe>();
        services.AddSingleton<RunActivityStore>();
        services.AddSingleton<TestGenRuns>();
        services.Configure<GitHubOptions>(configuration.GetSection(GitHubOptions.Section));
        services.AddHttpClient(GitHubIssues.HttpClientName);
        services.AddSingleton<GitHubIssues>();
        services.AddSingleton(sp => (GitRepository)sp.GetRequiredService<IRepository>());
        services.AddSingleton<RepoWriter>();
        services.AddSingleton<IRunVerifier, RunVerifier>();
        services.AddSingleton<CandidateDecisions>();
        services.AddSingleton<RunFollower>();
        services.AddHostedService(sp => sp.GetRequiredService<RunFollower>());
        CoverageRunnerRegistration.AddCoverageRunnerClient(services);
        services.AddSingleton<TestGenRunAgent>();
    }
    public void MapEndpoints(IMafEndpoints endpoints)
    {
        endpoints.Routes.MapCoverage();
        var runs = endpoints.Routes.ServiceProvider.GetRequiredService<TestGenRuns>();
        endpoints.MapPluginAgent("/api/coverage/runs/agent", endpoints.Routes.ServiceProvider.GetRequiredService<TestGenRunAgent>(),
            async (thread, ct) => TestGenRunAgent.RunIdOf(thread) is not { } id || await runs.GetAsync(id, ct) is null
                ? CoverageEndpoints.NotFound() : null);
    }
    public void ConfigureModel(ModelBuilder model)
    {
        model.Entity<CoverageSnapshotRow>().HasKey(x => x.Id);
        model.Entity<CoverageSnapshotRow>().HasIndex(x => new { x.Kind, x.CreatedAt });
        model.Entity<CoverageSnapshotRow>().HasIndex(x => x.RunId);
        model.Entity<CoverageFileRow>().HasKey(x => new { x.SnapshotId, x.Path });
        model.Entity<CoverageFileRow>().HasIndex(x => new { x.Path, x.SnapshotId });
        model.Entity<CoverageThresholdRow>().HasKey(x => x.Path);
        model.Entity<TestGenRunRow>().HasKey(x => x.Id);
        model.Entity<TestGenRunRow>().HasIndex(x => new { x.Path, x.CreatedAt });
        // At most one active run per file, enforced by the database across replicas.
        model.Entity<TestGenRunRow>().HasIndex(x => x.Path).IsUnique().HasFilter($"\"State\" IN ({TestGenRunState.ActiveSql})")
            .HasDatabaseName("IX_TestGenRuns_Path_Active");
        model.Entity<TestGenRunEventRow>().HasIndex(x => new { x.RunId, x.Seq }).IsUnique();
        model.Entity<TestGenIssueRow>().HasKey(x => new { x.RunId, x.TestKey });
        model.Entity<TestGenRunActivityRow>().HasKey(x => new { x.RunId, x.Seq });
        model.Entity<TestGenRunActivityRow>().HasIndex(x => new { x.RunId, x.LastSeq });
        model.Entity<CoverageSnapshotRow>().ToTable("CoverageSnapshots");
        model.Entity<CoverageFileRow>().ToTable("CoverageFiles");
        model.Entity<CoverageThresholdRow>().ToTable("CoverageThresholds");
        model.Entity<TestGenRunRow>().ToTable("TestGenRuns");
        model.Entity<TestGenRunEventRow>().ToTable("TestGenRunEvents");
        model.Entity<TestGenRunActivityRow>().ToTable("TestGenRunActivity");
        model.Entity<TestGenIssueRow>().ToTable("TestGenIssues");
    }
    public async Task MigrateDataAsync(DbContext db, CancellationToken ct)
    {
        // A run whose budget stopped an attempt used to be stored one attempt short (keep-attempt-on-budget-stop).
        await db.Database.ExecuteSqlRawAsync("""
            UPDATE "TestGenRuns"
            SET "Attempt" = (SELECT MAX(a."Attempt") FROM "TestGenRunActivity" a WHERE a."RunId" = "TestGenRuns"."Id")
            WHERE "Attempt" < (SELECT MAX(a."Attempt") FROM "TestGenRunActivity" a WHERE a."RunId" = "TestGenRuns"."Id")
            """, ct);
        // A run stored before its end was recorded (show-test-run-duration): its first update in a state that is not
        // running, else its last change. Running runs have no end; a row that has one is left alone.
        await db.Database.ExecuteSqlRawAsync("""
            UPDATE "TestGenRuns"
            SET "FinishedAt" = COALESCE(
                (SELECT MIN(e."At") FROM "TestGenRunEvents" e
                 WHERE e."RunId" = "TestGenRuns"."Id"
                   AND json_extract(e."Json", '$.state') NOT IN ('submitted', 'working', 'verifying')),
                "UpdatedAt")
            WHERE "FinishedAt" IS NULL AND "State" NOT IN ('submitted', 'working', 'verifying')
            """, ct);
    }
    public IOpenWork CreateOpenWork(IServiceProvider services) =>
        new CoverageOpenWork(services.GetRequiredService<IDbContextFactory<DbContext>>(),
            services.GetRequiredService<TestGenRuns>(), services.GetRequiredService<CoverageRefresher>());
}

internal sealed class CoverageOpenWork(IDbContextFactory<DbContext> db, TestGenRuns runs, CoverageRefresher refresh) : IOpenWork
{
    public async Task<IReadOnlyList<OpenWorkItem>> ListOpenAsync(CancellationToken ct)
    {
        await using var context = await db.CreateDbContextAsync(ct);
        var states = TestGenRunState.Running.ToArray();
        var result = await context.Set<TestGenRunRow>().AsNoTracking().Where(r => states.Contains(r.State))
            .Select(r => new OpenWorkItem("test-run", r.Id, r.State)).ToListAsync(ct);
        if (await refresh.CurrentAsync(ct) is { State: "running" } job)
            result.Add(new OpenWorkItem(CoverageRefresher.Kind, job.JobId, job.State));
        return result;
    }
    public async Task CancelAllAsync(CancellationToken ct)
    {
        foreach (var item in await ListOpenAsync(ct))
            if (item.Kind == "test-run") await runs.CancelAsync(item.Id, ct);
            else await refresh.CancelAsync(item.Id, ct);
    }
}
