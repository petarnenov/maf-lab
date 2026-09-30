using System.Collections.Concurrent;
using System.Threading.Channels;
using Maf.Lab.TestGen;
using Microsoft.Extensions.Options;

namespace Maf.Lab.CoverageRunner;

/// <summary>
/// Jobs in the order they came, run a few at a time. A caller learns its place in the queue and polls for the result;
/// a finished result is kept for a while and then forgotten. One replica: the queue is this process's.
/// </summary>
public sealed class JobQueue(JobExecutor executor, IOptions<RunnerOptions> options, TimeProvider time, ILogger<JobQueue> logger)
    : BackgroundService
{
    private sealed class Job(string id, RunnerRequest request, long order)
    {
        public string Id { get; } = id;
        public RunnerRequest Request { get; } = request;
        public long Order { get; } = order;
        public volatile string State = RunnerJobState.Queued;
        public RunnerResult? Result;
        public DateTimeOffset? DoneAt;
    }

    private readonly ConcurrentDictionary<string, Job> _jobs = new();
    private readonly Channel<Job> _queue = Channel.CreateUnbounded<Job>();
    private long _order;

    public RunnerJob Submit(RunnerRequest request)
    {
        Forget();
        var job = new Job($"job_{Guid.NewGuid():N}", request, Interlocked.Increment(ref _order));
        _jobs[job.Id] = job;
        _queue.Writer.TryWrite(job);
        return View(job);
    }

    public RunnerJob? Get(string id) => _jobs.TryGetValue(id, out var job) ? View(job) : null;

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(Enumerable.Range(0, Math.Max(1, options.Value.MaxConcurrent)).Select(_ => WorkAsync(stoppingToken)));

    private async Task WorkAsync(CancellationToken ct)
    {
        await foreach (var job in _queue.Reader.ReadAllAsync(ct))
        {
            job.State = RunnerJobState.Running;
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
            job.Result = result;
            job.DoneAt = time.GetUtcNow();
            job.State = RunnerJobState.Done;
        }
    }

    private RunnerJob View(Job job) => new(job.Id, job.State,
        job.State == RunnerJobState.Queued ? _jobs.Values.Count(j => j.State == RunnerJobState.Queued && j.Order < job.Order) + 1 : 0,
        job.State == RunnerJobState.Done ? job.Result : null);

    private void Forget()
    {
        var before = time.GetUtcNow() - options.Value.KeepResultsFor;
        foreach (var done in _jobs.Values.Where(j => j.DoneAt < before))
        {
            _jobs.TryRemove(done.Id, out _);
        }
    }
}
