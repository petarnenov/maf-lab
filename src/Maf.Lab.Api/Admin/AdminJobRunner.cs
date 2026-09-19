using System.Collections.Concurrent;
using Maf.Lab.Domain.Admin;

namespace Maf.Lab.Api.Admin;

/// <summary>Runs admin jobs (index, migrate) in the background, one per kind and firm at a time.</summary>
public sealed class AdminJobRunner(ILogger<AdminJobRunner> logger, TimeProvider time, IHostApplicationLifetime lifetime)
{
    private readonly ConcurrentDictionary<string, AdminJob> _jobs = new();
    private readonly ConcurrentDictionary<string, string> _active = new();

    public AdminJob Start(string firmId, string kind, Func<CancellationToken, Task<string>> work)
    {
        var key = $"{firmId}:{kind}";
        if (_active.TryGetValue(key, out var runningId) && _jobs.TryGetValue(runningId, out var running) && running.State is AdminJobStates.Queued or AdminJobStates.Running)
        {
            return running;
        }

        var job = new AdminJob($"j_{Guid.NewGuid():N}", kind, AdminJobStates.Running, time.GetUtcNow(), null, null);
        _jobs[job.JobId] = job;
        _active[key] = job.JobId;
        _ = Task.Run(async () =>
        {
            try
            {
                var summary = await work(lifetime.ApplicationStopping);
                _jobs[job.JobId] = job with { State = AdminJobStates.Succeeded, FinishedAt = time.GetUtcNow(), Summary = summary };
            }
            catch (Exception ex)
            {
                logger.LogError("admin job {Kind} failed: {ErrorType}", kind, ex.GetType().Name);
                _jobs[job.JobId] = job with { State = AdminJobStates.Failed, FinishedAt = time.GetUtcNow(), Summary = "The job failed; see server logs." };
            }
        });
        return job;
    }

    public AdminJob? Get(string jobId) => _jobs.GetValueOrDefault(jobId);

    public AdminJob? Current(string firmId) =>
        _jobs.Values.Where(j => _active.Any(a => a.Key.StartsWith(firmId + ":") && a.Value == j.JobId)).OrderByDescending(j => j.StartedAt).FirstOrDefault();
}
