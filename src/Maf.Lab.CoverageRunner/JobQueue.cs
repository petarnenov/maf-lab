using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Maf.Lab.Hosting;
using Maf.Lab.TestGen;
using Microsoft.Extensions.Options;

namespace Maf.Lab.CoverageRunner;

/// <summary>
/// Jobs in the order they came, run a few at a time. A caller learns its place in the queue and polls for the result;
/// a finished result is kept for a while and then forgotten. One replica: the queue is this process's.
/// <para>
/// An identical request is not run twice (coverage-runner, "An identical request reuses a complete result"): within
/// <see cref="RunnerOptions.ReuseResultsFor"/> a complete result this runner computed answers it at once, and while
/// one is queued or running the new job follows it. Only the runner's own results are ever kept; a caller supplies a
/// request, never a result.
/// </para>
/// </summary>
public sealed class JobQueue(JobExecutor executor, IOptions<RunnerOptions> options, TimeProvider time, ILogger<JobQueue> logger)
    : BackgroundService
{
    /// <summary>What reuse did with a request; the tag of <c>maf.runner.reuse</c> and of its log line.</summary>
    public static class ReuseOutcome
    {
        /// <summary>Answered with a kept result.</summary>
        public const string Hit = "hit";
        /// <summary>Waits for an identical job already queued or running.</summary>
        public const string Joined = "joined";
        /// <summary>Runs, and its complete result is kept.</summary>
        public const string Miss = "miss";
        /// <summary>Asked for a run of its own; runs, and its complete result replaces the kept one.</summary>
        public const string Fresh = "fresh";
        /// <summary>Not eligible: reuse is off, or the commit is not a full id.</summary>
        public const string Off = "off";
    }

    private sealed class Job(string id, RunnerRequest request, long order, System.Diagnostics.ActivityContext parent)
    {
        /// <summary>The trace of the request that submitted it: the job runs later, on a worker, but in that trace.</summary>
        public System.Diagnostics.ActivityContext Parent { get; } = parent;
        public string Id { get; } = id;
        public RunnerRequest Request { get; } = request;
        public long Order = order;
        public volatile string State = RunnerJobState.Queued;
        public RunnerResult? Result;
        public DateTimeOffset? DoneAt;

        /// <summary>The reuse key, when the job's result may be kept.</summary>
        public string? Key;

        /// <summary>The identical job this one waits for, while it does; guarded by the queue's gate.</summary>
        public Job? Leader;
        public List<Job> Followers { get; } = [];
    }

    private sealed record Kept(string JobId, DateTimeOffset At, RunnerResult Result);

    private readonly ConcurrentDictionary<string, Job> _jobs = new();
    private readonly Channel<Job> _queue = Channel.CreateUnbounded<Job>();
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Job> _inFlight = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Kept> _kept = new(StringComparer.Ordinal);
    private long _order;

    public RunnerJob Submit(RunnerRequest request)
    {
        Forget();
        var job = new Job($"job_{Guid.NewGuid():N}", request, Interlocked.Increment(ref _order),
            System.Diagnostics.Activity.Current?.Context ?? default);
        var key = Reusing ? KeyOf(request) : null;
        string outcome;
        lock (_gate)
        {
            var now = time.GetUtcNow();
            if (key is null)
            {
                outcome = ReuseOutcome.Off;
            }
            else if (request.Fresh)
            {
                // Its own sample: nobody follows it, and it follows nobody. A complete result still replaces the kept one.
                job.Key = key;
                outcome = ReuseOutcome.Fresh;
            }
            else if (_kept.TryGetValue(key, out var kept) && kept.At > now - options.Value.ReuseResultsFor)
            {
                job.Result = Reused(kept.Result, kept.JobId, kept.At);
                job.DoneAt = now;
                job.State = RunnerJobState.Done;
                outcome = ReuseOutcome.Hit;
            }
            else if (_inFlight.TryGetValue(key, out var leader))
            {
                job.Leader = leader;
                leader.Followers.Add(job);
                outcome = ReuseOutcome.Joined;
            }
            else
            {
                job.Key = key;
                _inFlight[key] = job;
                outcome = ReuseOutcome.Miss;
            }
            _jobs[job.Id] = job;
        }
        if (outcome is ReuseOutcome.Miss or ReuseOutcome.Fresh or ReuseOutcome.Off)
        {
            _queue.Writer.TryWrite(job);
        }
        var scope = request.Tests ?? TestScope.All;
        LabTelemetry.Instruments.RunnerReuse.Add(1, new KeyValuePair<string, object?>("outcome", outcome),
            new KeyValuePair<string, object?>("toolchain", request.Toolchain), new KeyValuePair<string, object?>("scope", scope));
        logger.LogInformation("runner reuse {Outcome} toolchain={Toolchain} scope={Scope}", outcome, request.Toolchain, scope);
        return View(job);
    }

    public RunnerJob? Get(string id) => _jobs.TryGetValue(id, out var job) ? View(job) : null;

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(Enumerable.Range(0, Math.Max(1, options.Value.MaxConcurrent)).Select(_ => WorkAsync(stoppingToken)));

    private bool Reusing => options.Value.ReuseResultsFor > TimeSpan.Zero && options.Value.ReuseMaxResults > 0;

    /// <summary>
    /// Everything a result depends on that a request names: the commit (a full id only — an abbreviated one is resolved
    /// at checkout), the toolchain, the diff, the target and the effective scope. The runner's own settings and image
    /// are fixed for the life of the process, and the kept results do not outlive it.
    /// </summary>
    internal static string? KeyOf(RunnerRequest request)
    {
        if (request.Commit.Length != 40 || !Git.IsCommitId(request.Commit))
        {
            return null;
        }
        var diff = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(request.Diff ?? "")));
        var text = string.Join('\n', request.Commit.ToLowerInvariant(), request.Toolchain, diff, request.TargetFile ?? "",
            request.Tests ?? TestScope.All);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private static RunnerResult Reused(RunnerResult result, string jobId, DateTimeOffset at) =>
        result with { ReusedFrom = new RunnerReuse(jobId, at) };

    private async Task WorkAsync(CancellationToken ct)
    {
        await foreach (var job in _queue.Reader.ReadAllAsync(ct))
        {
            job.State = RunnerJobState.Running;
            using var span = LabTelemetry.Source.StartActivity("runner.run", System.Diagnostics.ActivityKind.Internal, job.Parent);
            span?.SetTag("runner.toolchain", job.Request.Toolchain);
            RunnerResult result;
            try
            {
                result = await executor.RunAsync(job.Request, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogError("runner job failed ({ErrorType})", ex.GetType().Name);
                result = RunnerResult.Failed(RunnerStatus.Error, 0);
            }
            span?.SetTag("runner.status", result.Status);
            span?.SetTag("runner.build", result.Build);
            span?.SetTag("runner.tests.passed", result.Tests.Passed);
            span?.SetTag("runner.tests.failed", result.Tests.Failed);
            span?.SetTag("runner.duration_ms", result.DurationMs);
            span?.SetTag("runner.followers", job.Followers.Count);
            Finish(job, result);
        }
    }

    /// <summary>
    /// The job is done. A complete result is kept and answers every job that waited for it; otherwise the first of
    /// those is queued to run on its own and the rest wait for it in turn.
    /// </summary>
    private void Finish(Job job, RunnerResult result)
    {
        Job? next = null;
        lock (_gate)
        {
            var now = time.GetUtcNow();
            job.Result = result;
            job.DoneAt = now;
            job.State = RunnerJobState.Done;
            if (job.Key is not { } key)
            {
                return;
            }
            if (_inFlight.TryGetValue(key, out var leader) && leader == job)
            {
                _inFlight.Remove(key);
            }
            if (result.Reusable)
            {
                Keep(key, new Kept(job.Id, now, result));
                foreach (var follower in job.Followers)
                {
                    follower.Leader = null;
                    follower.Result = Reused(result, job.Id, now);
                    follower.DoneAt = now;
                    follower.State = RunnerJobState.Done;
                }
            }
            else if (job.Followers.Count > 0)
            {
                next = job.Followers[0];
                next.Leader = null;
                next.Key = key;
                next.Order = Interlocked.Increment(ref _order);
                foreach (var follower in job.Followers.Skip(1))
                {
                    follower.Leader = next;
                    next.Followers.Add(follower);
                }
                _inFlight[key] = next;
            }
            job.Followers.Clear();
        }
        if (next is not null)
        {
            _queue.Writer.TryWrite(next);
        }
    }

    private void Keep(string key, Kept kept)
    {
        _kept[key] = kept;
        while (_kept.Count > Math.Max(1, options.Value.ReuseMaxResults))
        {
            _kept.Remove(_kept.MinBy(k => k.Value.At).Key);
        }
    }

    private RunnerJob View(Job job)
    {
        lock (_gate)
        {
            // A job that waits for an identical one shows that job's state and place.
            var shown = job.Leader ?? job;
            var position = shown.State == RunnerJobState.Queued
                ? _jobs.Values.Count(j => j.Leader is null && j.State == RunnerJobState.Queued && j.Order < shown.Order) + 1
                : 0;
            // A leader and its followers finish under the same lock, so a follower never shows a finished leader.
            return new RunnerJob(job.Id, shown.State, position, job.State == RunnerJobState.Done ? job.Result : null);
        }
    }

    private void Forget()
    {
        var before = time.GetUtcNow() - options.Value.KeepResultsFor;
        foreach (var done in _jobs.Values.Where(j => j.DoneAt < before))
        {
            _jobs.TryRemove(done.Id, out _);
        }
        lock (_gate)
        {
            var stale = time.GetUtcNow() - options.Value.ReuseResultsFor;
            foreach (var key in _kept.Where(k => k.Value.At <= stale).Select(k => k.Key).ToList())
            {
                _kept.Remove(key);
            }
        }
    }
}
