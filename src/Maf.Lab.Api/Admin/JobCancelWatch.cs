using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Admin;
using Microsoft.EntityFrameworkCore;

namespace Maf.Lab.Api.Admin;

/// <summary>
/// Watches a running admin job's row and cancels <see cref="Token"/> once the row says canceled. The cancel may have
/// been taken by the other replica; the shared database is where it is recorded, so it needs no routing to the replica
/// running the job (stop-anything). A read that fails is tried again on the next tick.
/// </summary>
public sealed class JobCancelWatch : IAsyncDisposable
{
    private readonly CancellationTokenSource _canceled = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    private JobCancelWatch(IDbContextFactory<MafDbContext> db, string jobId, TimeSpan every, TimeProvider time)
    {
        _loop = WatchAsync(db, jobId, every, time);
    }

    public CancellationToken Token => _canceled.Token;

    public bool Canceled => _canceled.IsCancellationRequested;

    public static JobCancelWatch Start(IDbContextFactory<MafDbContext> db, string jobId, TimeSpan every, TimeProvider time) =>
        new(db, jobId, every, time);

    private async Task WatchAsync(IDbContextFactory<MafDbContext> db, string jobId, TimeSpan every, TimeProvider time)
    {
        using var timer = new PeriodicTimer(every, time);
        try
        {
            while (await timer.WaitForNextTickAsync(_stop.Token))
            {
                string? state;
                try
                {
                    await using var ctx = await db.CreateDbContextAsync(_stop.Token);
                    state = await ctx.AdminJobs.AsNoTracking().Where(j => j.Id == jobId).Select(j => j.State)
                        .FirstOrDefaultAsync(_stop.Token);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    continue;
                }
                if (state == AdminJobStates.Canceled)
                {
                    await _canceled.CancelAsync();
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await _loop;
        _stop.Dispose();
        _canceled.Dispose();
    }
}
