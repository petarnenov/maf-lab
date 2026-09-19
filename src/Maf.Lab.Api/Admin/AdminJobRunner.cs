using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Api.Admin;

public sealed class AdminJobOptions
{
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(10);
    /// <summary>A running job whose heartbeat is older than this is considered interrupted (its replica died).</summary>
    public TimeSpan StaleAfter { get; set; } = TimeSpan.FromSeconds(60);
}

/// <summary>
/// Runs admin jobs (index, migrate) with their state in the shared database, so any api replica can report a job's
/// status and at most one job per firm and kind runs at a time across replicas. The replica that starts a job runs it
/// and keeps its heartbeat fresh; a job whose heartbeat goes stale is reported failed ("interrupted").
/// </summary>
public sealed class AdminJobRunner(
    IDbContextFactory<MafDbContext> db,
    IOptions<AdminJobOptions> options,
    ILogger<AdminJobRunner> logger,
    TimeProvider time,
    IHostApplicationLifetime lifetime)
{
    public const string Interrupted = "The job was interrupted (its server instance stopped); start it again.";
    private readonly AdminJobOptions _options = options.Value;

    public string Instance { get; init; } = Environment.MachineName;

    public async Task<AdminJob> StartAsync(string firmId, string kind, Func<CancellationToken, Task<string>> work, CancellationToken ct)
    {
        await using var ctx = await db.CreateDbContextAsync(ct);
        await ExpireStaleAsync(ctx, firmId, ct);

        var now = time.GetUtcNow().UtcDateTime;
        var row = new AdminJobRow
        {
            Id = $"j_{Guid.NewGuid():N}",
            FirmId = firmId,
            Kind = kind,
            State = AdminJobStates.Running,
            StartedAt = now,
            HeartbeatAt = now,
            OwnerInstance = Instance,
        };
        ctx.AdminJobs.Add(row);
        try
        {
            await ctx.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Another request (possibly on another replica) already started this job: return it.
            await using var read = await db.CreateDbContextAsync(ct);
            var running = await read.AdminJobs.AsNoTracking()
                .FirstAsync(j => j.FirmId == firmId && j.Kind == kind && j.State == AdminJobStates.Running, ct);
            return ToContract(running);
        }

        _ = Task.Run(() => RunAsync(row.Id, kind, work));
        return ToContract(row);
    }

    public async Task<AdminJob?> GetAsync(string firmId, string jobId, CancellationToken ct)
    {
        await using var ctx = await db.CreateDbContextAsync(ct);
        await ExpireStaleAsync(ctx, firmId, ct);
        var row = await ctx.AdminJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId && j.FirmId == firmId, ct);
        return row is null ? null : ToContract(row);
    }

    public async Task<AdminJob?> CurrentAsync(string firmId, CancellationToken ct)
    {
        await using var ctx = await db.CreateDbContextAsync(ct);
        await ExpireStaleAsync(ctx, firmId, ct);
        var row = await ctx.AdminJobs.AsNoTracking().Where(j => j.FirmId == firmId).OrderByDescending(j => j.StartedAt).FirstOrDefaultAsync(ct);
        return row is null ? null : ToContract(row);
    }

    private async Task RunAsync(string jobId, string kind, Func<CancellationToken, Task<string>> work)
    {
        var stopping = lifetime.ApplicationStopping;
        using var heartbeatStop = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        var heartbeat = HeartbeatAsync(jobId, heartbeatStop.Token);
        string state, summary;
        try
        {
            summary = await work(stopping);
            state = AdminJobStates.Succeeded;
        }
        catch (Exception ex)
        {
            logger.LogError("admin job {Kind} failed: {ErrorType}", kind, ex.GetType().Name);
            (state, summary) = (AdminJobStates.Failed, "The job failed; see server logs.");
        }
        await heartbeatStop.CancelAsync();
        try
        {
            await heartbeat;
        }
        catch (OperationCanceledException)
        {
        }

        await using var ctx = await db.CreateDbContextAsync(CancellationToken.None);
        var row = await ctx.AdminJobs.FirstAsync(j => j.Id == jobId);
        row.State = state;
        row.Summary = summary;
        row.FinishedAt = time.GetUtcNow().UtcDateTime;
        await ctx.SaveChangesAsync();
    }

    private async Task HeartbeatAsync(string jobId, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(_options.HeartbeatInterval, time);
        while (await timer.WaitForNextTickAsync(ct))
        {
            await using var ctx = await db.CreateDbContextAsync(ct);
            await ctx.AdminJobs.Where(j => j.Id == jobId && j.State == AdminJobStates.Running)
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.HeartbeatAt, time.GetUtcNow().UtcDateTime), ct);
        }
    }

    /// <summary>Marks running jobs of the firm whose owner stopped heart-beating as failed, releasing the lock.</summary>
    private async Task ExpireStaleAsync(MafDbContext ctx, string firmId, CancellationToken ct)
    {
        var cutoff = time.GetUtcNow().UtcDateTime - _options.StaleAfter;
        var now = time.GetUtcNow().UtcDateTime;
        await ctx.AdminJobs.Where(j => j.FirmId == firmId && j.State == AdminJobStates.Running && j.HeartbeatAt < cutoff)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.State, AdminJobStates.Failed)
                .SetProperty(j => j.Summary, Interrupted)
                .SetProperty(j => j.FinishedAt, now), ct);
    }

    private static AdminJob ToContract(AdminJobRow r) => new(
        r.Id, r.Kind, r.State,
        new DateTimeOffset(DateTime.SpecifyKind(r.StartedAt, DateTimeKind.Utc)),
        r.FinishedAt is { } f ? new DateTimeOffset(DateTime.SpecifyKind(f, DateTimeKind.Utc)) : null,
        r.Summary);
}
