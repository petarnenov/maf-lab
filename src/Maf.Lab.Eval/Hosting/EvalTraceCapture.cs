using System.Collections.Concurrent;
using Maf.Lab.Domain.Tracing;
using Maf.Lab.Plugins.Abstractions;

namespace Maf.Lab.Eval.Hosting;

/// <summary>
/// The eval's own turn observer: it wants everything, as the monitor does, so an eval turn is traced exactly as before
/// the monitor became a plugin (the model capture, the prompt, retrieval diagnostics), and `make ask` prints the whole
/// timeline. In memory, per run, for the eval process only; nothing is evicted, which a short-lived CLI can afford.
/// </summary>
public sealed class EvalTraceCapture : ITurnObserver
{
    private readonly ConcurrentDictionary<string, List<TraceEvent>> _runs = new(StringComparer.Ordinal);

    public bool IsEnabled(string kind) => kind != RunFrames.Kind;

    public Task OnEventAsync(string runId, TraceEvent e, CancellationToken ct)
    {
        var events = _runs.GetOrAdd(runId, _ => []);
        lock (events)
        {
            events.Add(e);
        }
        return Task.CompletedTask;
    }

    public Task OnFramesAsync(string runId, string? turnId, IReadOnlyList<RunFrame> frames, CancellationToken ct) => Task.CompletedTask;

    /// <summary>A run's trace, once its observation has completed; empty for a run never observed.</summary>
    public IReadOnlyList<TraceEvent> Of(string runId) => _runs.TryGetValue(runId, out var events) ? [.. events] : [];
}
