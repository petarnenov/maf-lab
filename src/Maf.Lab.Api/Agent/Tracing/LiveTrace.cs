using System.Threading.Channels;
using Maf.Lab.Domain.SharedState;
using Maf.Lab.Domain.Tracing;

namespace Maf.Lab.Api.Agent.Tracing;

/// <summary>
/// Where a running turn's trace goes as it is written (agui-protocol-only): the shared store, so the monitor can read it
/// from any replica while the turn runs. The trace no longer travels on the run's AG-UI stream. Writing is queued and
/// kept in order, so a slow store never holds up the turn; <see cref="CompleteAsync"/> waits for the queue to drain.
/// </summary>
public sealed class LiveTrace
{
    private readonly Channel<TraceEvent> _queue = Channel.CreateUnbounded<TraceEvent>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Task _pump;

    public LiveTrace(IRunTraceStore store, string runId, ILogger logger)
    {
        _pump = Task.Run(async () =>
        {
            await foreach (var e in _queue.Reader.ReadAllAsync())
            {
                try
                {
                    await store.AppendAsync(runId, e, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    // The stored trace is still written with the turn; only the live view misses this event.
                    logger.LogWarning("live trace append failed ({ErrorType})", ex.GetType().Name);
                }
            }
        });
    }

    public void Write(TraceEvent e) => _queue.Writer.TryWrite(e);

    public Task CompleteAsync()
    {
        _queue.Writer.TryComplete();
        return _pump;
    }
}
