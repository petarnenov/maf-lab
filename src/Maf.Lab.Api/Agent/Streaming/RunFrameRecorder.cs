using System.Text;
using System.Text.Json;
using AGUI.Abstractions;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Tracing;
using Microsoft.EntityFrameworkCore;

namespace Maf.Lab.Api.Agent.Streaming;

/// <summary>
/// Keeps the frames of one run as they go out: every event, in the order it was written, including the ones a
/// client may ignore. Called from the one loop that puts events on the wire, so it is fed by a single thread.
/// </summary>
public sealed class RunFrameRecorder(TimeProvider? time = null)
{
    /// <summary>What the kept frames of one run may add up to. Past it, a frame keeps everything but its payload.</summary>
    public const int MaxBytes = 256 * 1024;

    /// <summary>What a frame costs once its payload is gone: the sequence, timing, type, name and size.</summary>
    private const int HeaderBytes = 96;

    private readonly long _startedAt = (time ?? TimeProvider.System).GetTimestamp();
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly List<RunFrame> _frames = [];
    private long _bytes;

    public IReadOnlyList<RunFrame> Frames => _frames;

    /// <summary>The turn this run was, once it has said so. A run that records no turn never sets it.</summary>
    public string? TurnId { get; private set; }

    public RunFrame Add(BaseEvent e)
    {
        var raw = AGUIStream.Serialize(e);
        var wireBytes = Encoding.UTF8.GetByteCount(raw);
        // Read from the serialized event rather than its properties: this is exactly what the client will see.
        var element = JsonSerializer.Deserialize<JsonElement>(raw);

        string? name = null;
        int? traceSeq = null;
        var carriesTrace = false;
        if (e is CustomEvent custom)
        {
            name = custom.Name;
            if (custom.Name == AGUIStream.TraceEvent && element.TryGetProperty("value", out var value))
            {
                carriesTrace = true;
                traceSeq = value.TryGetProperty("seq", out var s) && s.TryGetInt32(out var seq) ? seq : null;
                TurnId ??= TurnIdOf(value);
            }
        }
        else if (e is RunFinishedEvent && element.TryGetProperty("result", out var result)
            && result.ValueKind == JsonValueKind.Object && result.TryGetProperty("turnId", out var turn))
        {
            TurnId ??= turn.GetString();
        }

        // Only what is kept counts against the cap: a trace frame stores a pointer, not the trace event again.
        var kept = carriesTrace ? HeaderBytes : wireBytes;
        var truncated = _bytes + kept > MaxBytes;
        _bytes += truncated ? HeaderBytes : kept;

        var frame = new RunFrame(
            _frames.Count + 1,
            (long)_time.GetElapsedTime(_startedAt).TotalMilliseconds,
            AGUIStream.FrameName(e),
            name,
            wireBytes,
            traceSeq,
            carriesTrace || truncated ? null : element,
            truncated);
        _frames.Add(frame);
        return frame;
    }

    /// <summary>The turn a `turn.start` trace event names, so an errored or paused run keeps its frames too.</summary>
    private static string? TurnIdOf(JsonElement traceEvent) =>
        traceEvent.TryGetProperty("kind", out var kind) && kind.GetString() == TraceKinds.TurnStart
        && traceEvent.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
        && data.TryGetProperty("turnId", out var turnId)
            ? turnId.GetString()
            : null;
}

/// <summary>
/// Where a run's frames are kept: beside the turn's trace, so they are read by whoever may read it and deleted
/// when it is. A run that recorded no turn has nowhere to put them, and does not try.
/// </summary>
public sealed class RunFrameStore(IDbContextFactory<MafDbContext> db, ILogger<RunFrameStore> logger)
{
    public async Task SaveAsync(string? turnId, IReadOnlyList<RunFrame> frames, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(turnId) || frames.Count == 0)
        {
            return;
        }
        try
        {
            await using var ctx = await db.CreateDbContextAsync(ct);
            var json = JsonSerializer.Serialize(frames, AGUIStream.Json);
            await ctx.TurnTraces.Where(t => t.TurnId == turnId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.AguiJson, json), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The frames are a view of a turn, not the turn. Losing them must not fail the run that just ended.
            logger.LogWarning("could not store run frames for turn {TurnId}: {ErrorType}", turnId, ex.GetType().Name);
        }
    }
}
