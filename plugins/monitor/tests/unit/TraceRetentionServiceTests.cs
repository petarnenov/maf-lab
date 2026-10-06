using Maf.Lab.Plugins.Monitor;
using Maf.Lab.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Maf.Lab.Tests;

/// <summary>
/// The sweep behind the retention setting: it runs on a timer without being asked, says what it removed, survives a
/// sweep that failed, and stops when the host stops.
/// </summary>
public sealed class TraceRetentionServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly TimeSpan Sweep = TimeSpan.FromMilliseconds(20);

    private static string NewDbPath() =>
        Path.Combine(Directory.CreateTempSubdirectory("maf-retention-").FullName, "maf.db");

    private static DbContextOptions<MonitorDb> DbOptions(string path) =>
        new DbContextOptionsBuilder<MonitorDb>().UseSqlite($"Data Source={path}").Options;

    /// <summary>A factory of real contexts over one SQLite file, so a sweep can be watched end to end.</summary>
    private static IDbContextFactory<DbContext> Factory(DbContextOptions<MonitorDb> options)
    {
        var factory = Substitute.For<IDbContextFactory<DbContext>>();
        factory.CreateDbContextAsync(Arg.Any<CancellationToken>()).Returns(_ => new MonitorDb(options));
        return factory;
    }

    private static async Task<MonitorDb> InitializedAsync(DbContextOptions<MonitorDb> options)
    {
        var db = new MonitorDb(options);
        await db.Database.EnsureCreatedAsync(Ct);
        return db;
    }

    private static (TraceRetentionService Service, CapturingLoggerProvider Logs) Swept(IDbContextFactory<DbContext> db,
        TimeSpan? sweep = null)
    {
        var logs = new CapturingLoggerProvider();
        var logger = LoggerFactory.Create(b => b.AddProvider(logs).SetMinimumLevel(LogLevel.Debug))
            .CreateLogger<TraceRetentionService>();
        var service = new TraceRetentionService(db, Options.Create(new TracingOptions
        {
            RetentionDays = 7,
            SweepInterval = sweep ?? Sweep,
        }), TimeProvider.System, logger);
        return (service, logs);
    }

    private static TurnDiagnosticsRow TraceRow(string turnId, DateTime createdAt) => new()
    {
        TurnId = turnId, ConversationId = "c", UserId = "adam", TenantId = "firm-a", CreatedAt = createdAt, Json = "[]",
    };

    private static async Task WaitUntilAsync(Func<bool> there, string what)
    {
        for (var i = 0; i < 500; i++)
        {
            if (there()) return;
            await Task.Delay(10, Ct);
        }
        throw new TimeoutException(what);
    }

    /// <summary>StopAsync can return before the cancelled loop has wound down, so wait for it before judging it.</summary>
    private static async Task StoppedAsync(TraceRetentionService service, CancellationTokenSource cts)
    {
        await cts.CancelAsync();
        await service.StopAsync(cts.Token);
        await WaitUntilAsync(() => service.ExecuteTask!.IsCompleted, "the sweep never stopped");
    }

    [Fact]
    public void The_tracing_defaults_are_seven_days_swept_hourly()
    {
        var options = new TracingOptions();

        Assert.Equal(7, options.RetentionDays);
        Assert.Equal(TimeSpan.FromHours(1), options.SweepInterval);
        Assert.Equal("Tracing", TracingOptions.Section);
    }

    [Fact]
    public async Task A_sweep_removes_what_is_past_retention_says_how_much_and_keeps_going()
    {
        var options = DbOptions(NewDbPath());
        var factory = Factory(options);
        await using (var db = await InitializedAsync(options))
        {
            db.Set<TurnDiagnosticsRow>().Add(TraceRow("t_old_1", DateTime.UtcNow.AddDays(-8)));
            db.Set<TurnDiagnosticsRow>().Add(TraceRow("t_old_2", DateTime.UtcNow.AddDays(-30)));
            db.Set<TurnDiagnosticsRow>().Add(TraceRow("t_fresh", DateTime.UtcNow));
            await db.SaveChangesAsync(Ct);
        }

        var (service, logs) = Swept(factory);
        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);

        await WaitUntilAsync(() => logs.Messages.Any(m => m.Contains("trace retention removed 2 trace(s)")),
            "the first sweep never said what it removed");
        await using (var check = new MonitorDb(options))
        {
            Assert.False(await check.Set<TurnDiagnosticsRow>().AnyAsync(t => t.TurnId == "t_old_1" || t.TurnId == "t_old_2", Ct));
            Assert.True(await check.Set<TurnDiagnosticsRow>().AnyAsync(t => t.TurnId == "t_fresh", Ct));
        }

        // The sweep is not a one-shot: a trace that ages past retention on a later tick goes too.
        await using (var db = new MonitorDb(options))
        {
            db.Set<TurnDiagnosticsRow>().Add(TraceRow("t_late", DateTime.UtcNow.AddDays(-9)));
            await db.SaveChangesAsync(Ct);
        }
        await WaitUntilAsync(() => logs.Messages.Any(m => m.Contains("trace retention removed 1 trace(s)")),
            "the sweep never ran again");

        await StoppedAsync(service, cts);
        Assert.True(service.ExecuteTask!.IsCanceled);
    }

    [Fact]
    public async Task A_sweep_that_fails_is_reported_and_the_next_one_still_runs()
    {
        var options = DbOptions(NewDbPath());
        var factory = Substitute.For<IDbContextFactory<DbContext>>();
        var failures = 1;
        factory.CreateDbContextAsync(Arg.Any<CancellationToken>()).Returns(_ => failures-- > 0
            ? throw new InvalidOperationException("the database is down")
            : new MonitorDb(options));

        var (service, logs) = Swept(factory);
        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);

        await WaitUntilAsync(() => logs.Messages.Any(m => m.Contains("trace retention failed: InvalidOperationException")),
            "the failed sweep was never reported");

        // The database comes back: the next sweep does the work the failed one could not.
        await using (var db = await InitializedAsync(options))
        {
            db.Set<TurnDiagnosticsRow>().Add(TraceRow("t_back", DateTime.UtcNow.AddDays(-8)));
            await db.SaveChangesAsync(Ct);
        }
        await WaitUntilAsync(() => logs.Messages.Any(m => m.Contains("trace retention removed 1 trace(s)")),
            "the sweep never recovered after a failure");
        await using (var check = new MonitorDb(options))
        {
            Assert.False(await check.Set<TurnDiagnosticsRow>().AnyAsync(t => t.TurnId == "t_back", Ct));
        }

        await StoppedAsync(service, cts);
        Assert.True(service.ExecuteTask!.IsCanceled);
    }

    [Fact]
    public async Task Stopping_the_host_ends_the_sweep()
    {
        var options = DbOptions(NewDbPath());
        await using (var _ = await InitializedAsync(options))
        {
        }

        var (service, logs) = Swept(Factory(options), sweep: TimeSpan.FromHours(1));
        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);

        await StoppedAsync(service, cts);

        Assert.True(service.ExecuteTask!.IsCanceled);
        // An empty database means nothing to remove and nothing to report: the sweep stays silent until it works.
        Assert.DoesNotContain(logs.Messages, m => m.Contains("trace retention"));
    }

    [Fact]
    public async Task The_cutoff_is_the_retention_before_the_clock_and_the_boundary_itself_is_kept()
    {
        var now = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var time = Substitute.For<TimeProvider>();
        time.GetUtcNow().Returns(now);

        var options = DbOptions(NewDbPath());
        await using (var db = await InitializedAsync(options))
        {
            db.Set<TurnDiagnosticsRow>().Add(TraceRow("t_just_out", now.UtcDateTime.AddDays(-8)));
            db.Set<TurnDiagnosticsRow>().Add(TraceRow("t_on_the_line", now.UtcDateTime.AddDays(-7)));
            db.Set<TurnDiagnosticsRow>().Add(TraceRow("t_just_in", now.UtcDateTime.AddDays(-6)));
            await db.SaveChangesAsync(Ct);
        }

        var service = new TraceRetentionService(Factory(options), Options.Create(new TracingOptions { RetentionDays = 7 }),
            time, NullLogger<TraceRetentionService>.Instance);

        Assert.Equal(1, await service.PurgeAsync(Ct));

        await using var check = new MonitorDb(options);
        Assert.False(await check.Set<TurnDiagnosticsRow>().AnyAsync(t => t.TurnId == "t_just_out", Ct));
        // Older than the retention period is what goes; a trace exactly on the line, and one inside it, stay.
        Assert.True(await check.Set<TurnDiagnosticsRow>().AnyAsync(t => t.TurnId == "t_on_the_line", Ct));
        Assert.True(await check.Set<TurnDiagnosticsRow>().AnyAsync(t => t.TurnId == "t_just_in", Ct));
    }
}

/// <summary>The monitor's table alone, as the one store holds it once the plugin is installed.</summary>
public sealed class MonitorDb(DbContextOptions<MonitorDb> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => new MonitorPlugin().ConfigureModel(modelBuilder);
}
