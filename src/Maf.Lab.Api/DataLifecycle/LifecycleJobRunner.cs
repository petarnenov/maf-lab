using Maf.Lab.Domain.Tenancy;

namespace Maf.Lab.Api.DataLifecycle;

/// <summary>
/// Executes the persisted participant plan, awaiting every step before acknowledging a stop.
/// Internal execution primitive: its caller must establish legal-hold admission and quiesce writers
/// before starting work. It is deliberately not registered as a hosted service or exposed by a route.
/// A lost server is never assumed dead merely because it stopped reporting progress.
/// </summary>
internal sealed class LifecycleJobRunner(LifecycleJobStore jobs, TimeProvider time, TimeSpan pollInterval)
{
    private readonly TimeSpan pollEvery = pollInterval > TimeSpan.Zero
        ? pollInterval : throw new ArgumentOutOfRangeException(nameof(pollInterval));

    /// <summary>
    /// The executor must await all effects, including cancellation cleanup, before returning. A step
    /// interrupted before its checkpoint is replayed, so its effects must be idempotent. Export executors
    /// use the persisted generation for private staging and publish only a complete bundle.
    /// </summary>
    internal async Task<LifecycleJobSnapshot> RunAsync(TenantId tenant, string id,
        Func<LifecycleJobSnapshot, string, CancellationToken, Task> execute, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(execute);
        var job = await jobs.ClaimAsync(tenant, id, ct);
        var attempt = job.AttemptId!;
        using var run = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var watchStop = new CancellationTokenSource();
        var watch = WatchAsync(tenant, id, attempt, run, watchStop.Token);
        var stopped = false;
        var failed = false;
        try
        {
            for (var index = job.CompletedParticipants; index < job.Participants.Count; index++)
            {
                run.Token.ThrowIfCancellationRequested();
                await execute(job, job.Participants[index], run.Token);
                job = await jobs.CheckpointAsync(tenant, id, attempt, job.Participants[index], run.Token);
                if (job.State == LifecycleJobState.Stopping)
                {
                    stopped = true;
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (run.IsCancellationRequested)
        {
            stopped = true;
        }
        catch (Exception)
        {
            // Exceptions can contain exported content or credentials. Persist only the state here.
            failed = true;
        }
        finally
        {
            await watchStop.CancelAsync();
        }
        var watchFailed = await watch;
        // The participant has now fully unwound. A canceled request must not prevent recording that
        // fact. If this persistence fails, the active slot stays held for explicit recovery.
        if (failed || watchFailed)
            return await jobs.FailAsync(tenant, id, attempt, CancellationToken.None);
        if (stopped || ct.IsCancellationRequested)
            return await jobs.AcknowledgeStoppedAsync(tenant, id, attempt, CancellationToken.None);
        return await jobs.SucceedAsync(tenant, id, attempt, CancellationToken.None);
    }

    private async Task<bool> WatchAsync(TenantId tenant, string id, string attempt,
        CancellationTokenSource run, CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(pollEvery, time);
            do
            {
                var current = await jobs.GetAsync(tenant, id, ct);
                if (current is null || current.AttemptId != attempt
                    || current.State is not (LifecycleJobState.Running or LifecycleJobState.Stopping))
                {
                    await run.CancelAsync();
                    return true;
                }
                if (current.State == LifecycleJobState.Stopping)
                {
                    await run.CancelAsync();
                    return false;
                }
            }
            while (await timer.WaitForNextTickAsync(ct));
            return false;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception)
        {
            // Without the shared state we cannot safely admit another step. Cancel, then await the
            // current executor before recording failure; never release the slot from this watcher.
            await run.CancelAsync();
            return true;
        }
    }
}
