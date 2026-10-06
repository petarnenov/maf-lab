using System.Threading.Channels;
using Maf.Lab.Domain.Tracing;
using Maf.Lab.Plugins.Abstractions;

namespace Maf.Lab.Api.Agent.Tracing;

/// <summary>
/// The turn observers of the installed plugins, as one (GoF Composite; introduce-plugins decision 7). With none installed
/// a turn is observed by nobody: no live trace is written and nothing optional is built. Asked by name before the core
/// pays for an optional record (<see cref="IsEnabled"/>), as DiagnosticListener and ILogger are.
/// </summary>
public sealed class TurnObservers(IEnumerable<ITurnObserver> observers, ILogger<TurnObservers> logger)
{
    /// <summary>How long a run's end waits for its observers to take what it wrote; a stopped run's last events included.</summary>
    public static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(5);

    private readonly IReadOnlyList<ITurnObserver> _observers = [.. observers];

    /// <summary>A host with no observer: what a turn outside the api (a test, a tool) runs with.</summary>
    public static TurnObservers None { get; } = new([], Microsoft.Extensions.Logging.Abstractions.NullLogger<TurnObservers>.Instance);

    /// <summary>Whether any observer wants events or frames of this kind.</summary>
    public bool IsEnabled(string kind) => _observers.Any(o => Safe(() => o.IsEnabled(kind)));

    /// <summary>Starts observing one run: each observer gets its own in-order queue, drained off the turn's path.</summary>
    public TurnObservation Begin(string runId) => new(runId, _observers, logger);

    /// <summary>The run's AG-UI frames, to every observer that wants them; awaited with the bounded drain.</summary>
    public async Task FramesAsync(string runId, string? turnId, IReadOnlyList<RunFrame> frames)
    {
        foreach (var observer in _observers.Where(o => Safe(() => o.IsEnabled(RunFrames.Kind))))
        {
            try
            {
                await observer.OnFramesAsync(runId, turnId, frames, CancellationToken.None).WaitAsync(DrainTimeout);
            }
            catch (Exception ex)
            {
                logger.LogWarning("turn observer failed on frames ({ErrorType})", ex.GetType().Name);
            }
        }
    }

    private bool Safe(Func<bool> ask)
    {
        try
        {
            return ask();
        }
        catch (Exception ex)
        {
            logger.LogWarning("turn observer failed on IsEnabled ({ErrorType})", ex.GetType().Name);
            return false;
        }
    }
}

/// <summary>
/// One run as its observers receive it. <see cref="Write"/> never waits: each observer has an unbounded queue and a pump
/// that hands it the events in order, so a slow or failing observer never holds up the turn. <see cref="CompleteAsync"/>
/// waits, with <see cref="CancellationToken.None"/> and a bounded timeout, for the queues to drain, so a stopped run
/// still delivers its last events (its cancelled turn.end) — and nothing outlives the request beyond that.
/// </summary>
public sealed class TurnObservation
{
    private readonly List<(ITurnObserver Observer, Channel<TraceEvent> Queue, Task Pump)> _sinks = [];

    internal TurnObservation(string runId, IReadOnlyList<ITurnObserver> observers, ILogger logger)
    {
        foreach (var observer in observers)
        {
            var queue = Channel.CreateUnbounded<TraceEvent>(new UnboundedChannelOptions { SingleReader = true });
            var pump = Task.Run(async () =>
            {
                await foreach (var e in queue.Reader.ReadAllAsync())
                {
                    try
                    {
                        if (observer.IsEnabled(e.Kind))
                        {
                            await observer.OnEventAsync(runId, e, CancellationToken.None);
                        }
                    }
                    catch (Exception ex)
                    {
                        // The turn and its core record are unaffected; only this observer misses the event.
                        logger.LogWarning("turn observer failed ({ErrorType})", ex.GetType().Name);
                    }
                }
            });
            _sinks.Add((observer, queue, pump));
        }
    }

    public void Write(TraceEvent e)
    {
        foreach (var (_, queue, _) in _sinks)
        {
            queue.Writer.TryWrite(e);
        }
    }

    public async Task CompleteAsync()
    {
        foreach (var (_, queue, _) in _sinks)
        {
            queue.Writer.TryComplete();
        }
        try
        {
            await Task.WhenAll(_sinks.Select(s => s.Pump)).WaitAsync(TurnObservers.DrainTimeout);
        }
        catch (TimeoutException)
        {
            // An observer that has not caught up keeps its own pace; the request does not wait for it any longer.
        }
    }
}
