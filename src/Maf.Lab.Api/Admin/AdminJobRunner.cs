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
    /// <summary>How often a running job looks at its row for a cancel another replica recorded (stop-anything).</summary>
    public TimeSpan CancelPollEvery { get; set; } = TimeSpan.FromSeconds(1);
}

/// <summary>What a cancel found: the job stopping, a job that had already ended, or none at all.</summary>
public enum AdminJobCancel
{
    Canceling,
    AlreadyEnded,
    NotFound,
}

/// <summary>A job that failed for a reason its summary may name: the message is shown to the administrator.</summary>
public sealed class AdminJobFailure(string reason, Exception? inner = null) : Exception(reason, inner);

/// <summary>
/// Runs admin jobs (index, migrate) with their state in the shared database, so any api replica can report a job's
/// status and at most one job per firm and kind runs at a time across replicas. The replica that starts a job runs it
/// and keeps its heartbeat fresh; a job whose heartbeat goes stale is reported failed ("interrupted").
///
/// A job can be stopped (stop-anything) through any replica: the cancel is the job's row moving to canceled, and the
/// replica running the job watches its row and stops the work. Every state change is one statement guarded on the job
/// still running, so nothing written later — the work's own end, a stale-heartbeat sweep — undoes a cancel.
/// </summary>
public sealed class AdminJobRunner(
    IDbContextFactory<MafDbContext> db,
    IOptions<AdminJobOptions> options,
    ILogger<AdminJobRunner> logger,
    TimeProvider time,
    IHostApplicationLifetime lifetime)
{
    public const string Interrupted = "The job was interrupted (its server instance stopped); start it again.";
    public const string CanceledSummary = "Canceled by an administrator.";
    private readonly AdminJobOptions _options = options.Value;

    public string Instance { get; init; } = Environment.MachineName;

    public Task<AdminJob> StartAsync(string firmId, string kind, Func<CancellationToken, Task<string>> work, CancellationToken ct) =>
        StartAsync(firmId, kind, (_, token) => work(token), ct);

    /// <summary>Starts a job whose work reports how far it has got, so a canceled job can say so.</summary>
    public async Task<AdminJob> StartAsync(string firmId, string kind, Func<IProgress<string>, CancellationToken, Task<string>> work,
        CancellationToken ct)
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

    /// <summary>
    /// Cancels a running job of the firm, whichever replica runs it: one guarded update moves its row to canceled, and
    /// the replica running it stops the work when it sees that.
    /// </summary>
    public async Task<(AdminJobCancel Outcome, AdminJob? Job)> CancelAsync(string firmId, string jobId, CancellationToken ct)
    {
        await using var ctx = await db.CreateDbContextAsync(ct);
        var now = time.GetUtcNow().UtcDateTime;
        var canceled = await ctx.AdminJobs
            .Where(j => j.Id == jobId && j.FirmId == firmId && j.State == AdminJobStates.Running)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.State, AdminJobStates.Canceled)
                .SetProperty(j => j.Summary, CanceledSummary)
                .SetProperty(j => j.FinishedAt, now), ct);
        var row = await ctx.AdminJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId && j.FirmId == firmId, ct);
        if (row is null)
        {
            return (AdminJobCancel.NotFound, null);
        }
        return (canceled == 1 ? AdminJobCancel.Canceling : AdminJobCancel.AlreadyEnded, ToContract(row));
    }

    public async Task<AdminJob?> CurrentAsync(string firmId, CancellationToken ct)
    {
        await using var ctx = await db.CreateDbContextAsync(ct);
        await ExpireStaleAsync(ctx, firmId, ct);
        var row = await ctx.AdminJobs.AsNoTracking().Where(j => j.FirmId == firmId).OrderByDescending(j => j.StartedAt).FirstOrDefaultAsync(ct);
        return row is null ? null : ToContract(row);
    }

    private async Task RunAsync(string jobId, string kind, Func<IProgress<string>, CancellationToken, Task<string>> work)
    {
        var stopping = lifetime.ApplicationStopping;
        using var heartbeatStop = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        var heartbeat = HeartbeatAsync(jobId, heartbeatStop.Token);
        // The cancel is the row; this replica learns of it there, whichever replica took it.
        await using var watch = JobCancelWatch.Start(db, jobId, _options.CancelPollEvery, time);
        using var run = CancellationTokenSource.CreateLinkedTokenSource(stopping, watch.Token);
        var progress = new LatestProgress();
        string state, summary;
        try
        {
            summary = await work(progress, run.Token);
            state = AdminJobStates.Succeeded;
        }
        catch (OperationCanceledException) when (watch.Canceled)
        {
            logger.LogInformation("admin job {Kind} canceled", kind);
            (state, summary) = (AdminJobStates.Canceled,
                progress.Latest is { } reached ? $"{CanceledSummary} It had {reached}." : CanceledSummary);
        }
        catch (Exception ex)
        {
            logger.LogError("admin job {Kind} failed: {ErrorType}", kind, ex.GetType().Name);
            // Say why in words a person can act on: the service stopping is not the job failing.
            (state, summary) = (AdminJobStates.Failed,
                stopping.IsCancellationRequested ? Interrupted
                : ex is AdminJobFailure failure ? failure.Message
                : "The job failed; see server logs.");
        }
        await heartbeatStop.CancelAsync();
        try
        {
            await heartbeat;
        }
        catch (OperationCanceledException)
        {
        }

        // Guarded like every change: a job canceled meanwhile keeps its cancel; its own end only adds how far it got.
        await using var ctx = await db.CreateDbContextAsync(CancellationToken.None);
        var now = time.GetUtcNow().UtcDateTime;
        var from = state == AdminJobStates.Canceled ? AdminJobStates.Canceled : AdminJobStates.Running;
        await ctx.AdminJobs.Where(j => j.Id == jobId && (j.State == AdminJobStates.Running || j.State == from))
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.State, state)
                .SetProperty(j => j.Summary, summary)
                .SetProperty(j => j.FinishedAt, j => j.FinishedAt ?? now));
    }

    /// <summary>The last thing the work said about how far it has got.</summary>
    private sealed class LatestProgress : IProgress<string>
    {
        private string? latest;
        public string? Latest => Volatile.Read(ref latest);
        public void Report(string value) => Volatile.Write(ref latest, value);
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
