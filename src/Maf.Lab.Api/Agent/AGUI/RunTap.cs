using System.IO.Pipelines;
using System.Net.ServerSentEvents;
using System.Text;
using System.Text.Json;
using Maf.Lab.Api.Agent.Streaming;
using Maf.Lab.Domain.SharedState;

namespace Maf.Lab.Api.Agent.AGUI;

/// <summary>
/// Watches what the official AG-UI server writes for a chat run, without touching it (agui-protocol-only): the run's
/// frames, kept with its turn, and its snapshot, kept where every replica can read it for a rejoin. The response is
/// copied as it goes out and decoded with the platform's SSE parser; nothing here writes to the stream.
/// </summary>
public sealed class RunTap(RequestDelegate next, ILogger<RunTap> logger)
{
    public async Task InvokeAsync(HttpContext context, IRunStateStore runStates, Tracing.TurnObservers observers, TimeProvider time)
    {
        if (!HttpMethods.IsPost(context.Request.Method) || context.Request.Path != ChatAgentEndpoint.Path)
        {
            await next(context);
            return;
        }

        var body = context.Response.Body;
        var pipe = new Pipe();
        // The run's frames are kept only when an observer wants them (the monitor); the run state is recorded either way.
        var frames = observers.IsEnabled(Maf.Lab.Plugins.Abstractions.RunFrames.Kind) ? new RunFrameRecorder(time) : null;
        RunStateTracker? state = null;
        var reader = Task.Run(async () =>
        {
            await using var events = pipe.Reader.AsStream();
            await foreach (var item in SseParser.Create(events).EnumerateAsync())
            {
                // The filter has run by the time the server writes anything, so the run is known.
                if (state is null && context.Items[ChatRunFilter.RunKey] is ChatRun run)
                {
                    state = new RunStateTracker(runStates, run.Principal, run.ConversationId, run.RunId,
                        run.RecordsNoTurn ? null : run.RunId, time);
                }
                JsonElement e;
                try
                {
                    e = JsonDocument.Parse(item.Data).RootElement.Clone();
                }
                catch (JsonException)
                {
                    continue;
                }
                frames?.Add(e, Encoding.UTF8.GetByteCount(item.Data));
                if (state?.Observe(e) == true)
                {
                    await state.SaveAsync(CancellationToken.None);
                }
            }
        });

        context.Response.Body = new TeeStream(body, pipe.Writer);
        try
        {
            await next(context);
        }
        finally
        {
            context.Response.Body = body;
            await pipe.Writer.CompleteAsync();
            try
            {
                await reader;
            }
            catch (Exception ex)
            {
                logger.LogWarning("run tap failed ({ErrorType})", ex.GetType().Name);
            }
            // A run stopped before the server wrote its first frame (say, while a Jev check was out) still ends
            // cancelled for every replica, not running.
            if (state is null && context.RequestAborted.IsCancellationRequested
                && context.Items[ChatRunFilter.RunKey] is ChatRun stopped)
            {
                state = new RunStateTracker(runStates, stopped.Principal, stopped.ConversationId, stopped.RunId,
                    stopped.RecordsNoTurn ? null : stopped.RunId, time);
            }
            if (context.Items[ChatRunFilter.RunKey] is ChatRun run && state is not null)
            {
                // A response that ends with no terminal event is a run that was stopped: the client's abort may not
                // have reached RequestAborted yet when the stopped work unwinds. A run that ended keeps its outcome.
                state.Cancelled();
                // What the client saw last, whether the run ended or the client walked away from it.
                await state.SaveAsync(CancellationToken.None);
                // What the client received, handed to the observers that keep it, under the turn the run recorded.
                if (frames is not null)
                {
                    await observers.FramesAsync(run.RunId, run.RecordsNoTurn ? null : run.RunId, frames.Frames);
                }
            }
        }
    }

    /// <summary>Writes to the response and, unchanged, to the tap.</summary>
    private sealed class TeeStream(Stream inner, PipeWriter copy) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() => inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await inner.WriteAsync(buffer, cancellationToken);
            await copy.WriteAsync(buffer, CancellationToken.None);
        }
    }
}
