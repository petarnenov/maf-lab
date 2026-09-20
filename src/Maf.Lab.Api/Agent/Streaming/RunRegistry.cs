using System.Collections.Concurrent;

namespace Maf.Lab.Api.Agent.Streaming;

/// <summary>
/// The runs this instance is streaming, so one can be stopped by name.
///
/// It is per-instance on purpose. The balancer spreads requests, so a stop sent to the replica that is not
/// running the turn finds nothing — and that is reported rather than hidden. What a browser actually does is
/// abandon the stream, which cancels the same token through the request itself and always works.
/// </summary>
public sealed class RunRegistry
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _runs = new(StringComparer.Ordinal);

    public Registration Start(string runId, CancellationToken requestAborted)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
        _runs[runId] = source;
        return new Registration(this, runId, source);
    }

    /// <summary>True when a run by that name was running here and has been asked to stop.</summary>
    public bool Stop(string runId)
    {
        if (!_runs.TryGetValue(runId, out var source))
        {
            return false;
        }
        source.Cancel();
        return true;
    }

    public bool IsRunning(string runId) => _runs.ContainsKey(runId);

    public sealed class Registration(RunRegistry registry, string runId, CancellationTokenSource source) : IDisposable
    {
        public CancellationToken Token => source.Token;

        public void Dispose()
        {
            registry._runs.TryRemove(runId, out _);
            source.Dispose();
        }
    }
}
