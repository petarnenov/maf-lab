using System.Collections.Concurrent;
using A2A;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Plugins.Coverage;

/// <summary>What happens to a run once its task has completed: checking what the agent did (task 8.1).</summary>
public interface IRunVerifier
{
    Task VerifyAsync(string runId, CancellationToken ct);
}

/// <summary>
/// Follows every run whose task is with the agent, from whichever replica holds its lease. A lease is a row in the
/// shared database, renewed while the replica follows; a replica that stops stops renewing, and another takes the run
/// over from its task id. Following is a subscription, resumed when it drops and replaced by polling while it cannot
/// be had. Every update is written before it is relayed.
///
/// Stopping (stop-anything) is the generic host's: the stopping token ends the sweep, so no run is taken after it, and
/// every run in hand sees the same token; <see cref="StopAsync"/> then waits, within the host's shutdown timeout, for
/// those runs' last writes, before the host disposes what they write with.
/// </summary>
public sealed class RunFollower(
    IDbContextFactory<DbContext> dbFactory,
    TestGenRuns runs,
    TestAgentClient agent,
    IRunVerifier verifier,
    IOptions<TestAgentOptions> options,
    TimeProvider time,
    ILogger<RunFollower> logger) : BackgroundService
{
    /// <summary>This replica's name in a lease.</summary>
    public string Instance { get; init; } = $"{Environment.MachineName}-{Guid.NewGuid():N}";

    private readonly HashSet<string> _following = [];
    private readonly ConcurrentDictionary<Task, byte> _inFlight = new();

    /// <summary>How many runs this replica is following right now, their last writes included.</summary>
    internal int InFlight => _inFlight.Count;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // No agent, no runs to follow: a host without one does not poll for them.
        if (string.IsNullOrWhiteSpace(options.Value.BaseUrl))
        {
            return;
        }
        using var timer = new PeriodicTimer(options.Value.FollowerPollEvery, time);
        do
        {
            try
            {
                await ClaimAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("run follower sweep failed ({ErrorType})", ex.GetType().Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Takes every run that needs following and has no live follower, and follows it.</summary>
    internal async Task ClaimAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var stale = now - options.Value.FollowerStaleAfter;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var candidates = await db.Set<TestGenRunRow>().AsNoTracking()
            .Where(r => (r.State == TestGenRunState.Submitted || r.State == TestGenRunState.Working || r.State == TestGenRunState.Verifying)
                && r.TaskId != null
                && (r.Follower == null || r.FollowerHeartbeatAt < stale || r.Follower == Instance))
            .Select(r => r.Id).ToListAsync(ct);
        foreach (var id in candidates)
        {
            lock (_following)
            {
                if (_following.Contains(id))
                {
                    continue;
                }
            }
            // The lease is taken by a conditional update: of two replicas trying at once, one changes a row.
            var taken = await db.Set<TestGenRunRow>()
                .Where(r => r.Id == id && (r.Follower == null || r.FollowerHeartbeatAt < stale || r.Follower == Instance))
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Follower, Instance).SetProperty(r => r.FollowerHeartbeatAt, now), ct);
            if (taken == 0)
            {
                continue;
            }
            lock (_following)
            {
                _following.Add(id);
            }
            var follow = Task.Run(() => FollowAsync(id, ct), CancellationToken.None);
            _inFlight.TryAdd(follow, 0);
            _ = follow.ContinueWith(done => _inFlight.TryRemove(done, out _), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    /// <summary>Ends the sweep, then waits for the runs in hand to finish their last writes, within the host's timeout.</summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        try
        {
            await Task.WhenAll(_inFlight.Keys).WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("run follower stopped with {Count} runs still ending (shutdown timeout)", _inFlight.Count);
        }
    }

    private async Task FollowAsync(string runId, CancellationToken stopping)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        using var renew = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        var heartbeat = HeartbeatAsync(runId, renew.Token);
        var outcome = "stopped";
        try
        {
            outcome = await FollowTaskAsync(runId, stopping);
            if (outcome == TestGenRunState.Verifying)
            {
                await verifier.VerifyAsync(runId, stopping);
                outcome = (await runs.GetAsync(runId, stopping))?.State ?? outcome;
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError("following run {RunId} failed ({ErrorType})", runId, ex.GetType().Name);
            outcome = "error";
        }
        finally
        {
            await renew.CancelAsync();
            try
            {
                await heartbeat;
            }
            catch (OperationCanceledException)
            {
            }
            lock (_following)
            {
                _following.Remove(runId);
            }
            if (await runs.GetAsync(runId, CancellationToken.None) is { TaskId: { } taskId })
            {
                await agent.RecordFollowAsync(runId, taskId, outcome, watch.Elapsed, CancellationToken.None);
            }
        }
    }

    /// <summary>Follows the task until the run leaves the agent's hands; returns the run's state then.</summary>
    private async Task<string> FollowTaskAsync(string runId, CancellationToken ct)
    {
        while (true)
        {
            var run = await runs.GetAsync(runId, ct);
            if (run is null)
            {
                return "gone";
            }
            if (run.State is not (TestGenRunState.Submitted or TestGenRunState.Working))
            {
                return run.State;
            }
            var runDeadline = TestGenRuns.DeadlineOf(run, options.Value);
            if (time.GetUtcNow().UtcDateTime - run.CreatedAt > runDeadline)
            {
                try
                {
                    await agent.CancelAsync(runId, run.TaskId!, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning("deadline cancel of run {RunId} did not reach the agent ({ErrorType})", runId, ex.GetType().Name);
                }
                return (await runs.FinishAsync(runId, TestGenRunState.Failed, "deadline", ct))?.State ?? TestGenRunState.Failed;
            }

            var taskId = run.TaskId!;
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
                deadline.CancelAfter(runDeadline - (time.GetUtcNow().UtcDateTime - run.CreatedAt) + TimeSpan.FromSeconds(1));
                await foreach (var seen in agent.SubscribeAsync(taskId, deadline.Token))
                {
                    run = await runs.ApplyAsync(runId, seen, ct);
                    if (run is null || run.State is not (TestGenRunState.Submitted or TestGenRunState.Working))
                    {
                        return run?.State ?? "gone";
                    }
                }
                // The stream ended without an end: look, then subscribe again.
                run = await runs.ApplyAsync(runId, await agent.GetAsync(taskId, ct), ct);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // The deadline fell during the stream; the loop's check ends the run.
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // No stream to be had: poll until one can be had again.
                logger.LogInformation("run {RunId} stream unavailable, polling ({ErrorType})", runId, ex.GetType().Name);
                agent.Forget();
                try
                {
                    run = await runs.ApplyAsync(runId, await agent.GetAsync(taskId, ct), ct);
                }
                catch (Exception pollError) when (pollError is not OperationCanceledException)
                {
                    logger.LogInformation("run {RunId} poll failed ({ErrorType})", runId, pollError.GetType().Name);
                }
            }
            if (run is not null && run.State is not (TestGenRunState.Submitted or TestGenRunState.Working))
            {
                return run.State;
            }
            await Task.Delay(options.Value.ResubscribeAfter, time, ct);
        }
    }

    private async Task HeartbeatAsync(string runId, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(options.Value.FollowerPollEvery, time);
        while (await timer.WaitForNextTickAsync(ct))
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            await db.Set<TestGenRunRow>().Where(r => r.Id == runId && r.Follower == Instance)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.FollowerHeartbeatAt, time.GetUtcNow().UtcDateTime), ct);
        }
    }
}
