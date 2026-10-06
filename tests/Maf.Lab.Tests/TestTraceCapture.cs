using System.Collections.Concurrent;
using Maf.Lab.Domain.Tracing;
using Maf.Lab.Plugins.Abstractions;

namespace Maf.Lab.Tests;

/// <summary>
/// A turn observer for the core's tests (introduce-plugins 5.3): it wants everything, as the monitor does, so a test
/// host traces a turn exactly as before the monitor became a plugin, and <see cref="ApiFactory.TracesOf"/> reads the
/// very TraceEvent elements the turn wrote. Keyed by run id across the test run, as the fake shared stores are.
/// </summary>
public sealed class TestTraceCapture : ITurnObserver
{
    public static readonly ConcurrentDictionary<string, List<TraceEvent>> All = new(StringComparer.Ordinal);

    public bool IsEnabled(string kind) => true;

    public Task OnEventAsync(string runId, TraceEvent e, CancellationToken ct)
    {
        var events = All.GetOrAdd(runId, _ => []);
        lock (events)
        {
            events.Add(e);
        }
        return Task.CompletedTask;
    }

    public Task OnFramesAsync(string runId, string? turnId, IReadOnlyList<RunFrame> frames, CancellationToken ct) => Task.CompletedTask;
}
