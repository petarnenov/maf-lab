using System.Text.Json;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Tracing;
using Microsoft.EntityFrameworkCore;

namespace Maf.Lab.Api.Agent.Streaming;

/// <summary>
/// Keeps the frames of one run as they went out: every event, in the order it was written, including the ones a client
/// may ignore. It is fed what the official AG-UI server actually wrote, as JSON (<see cref="AGUI.RunTap"/>), by a single
/// reader. A frame's <c>name</c> and <c>traceSeq</c> belonged to custom events and stay empty (agui-protocol-only); turns
/// recorded before keep them.
/// </summary>
public sealed class RunFrameRecorder(TimeProvider? time = null)
{
    /// <summary>What the kept frames of one run may add up to. Past it, a frame keeps everything but its payload.</summary>
    public const int MaxBytes = 256 * 1024;

    /// <summary>What a frame costs once its payload is gone: the sequence, timing, type and size.</summary>
    private const int HeaderBytes = 96;

    private readonly long _startedAt = (time ?? TimeProvider.System).GetTimestamp();
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly List<RunFrame> _frames = [];
    private long _bytes;

    public IReadOnlyList<RunFrame> Frames => _frames;

    /// <param name="wireBytes">The event's size as written.</param>
    public RunFrame Add(JsonElement e, int wireBytes)
    {
        var truncated = _bytes + wireBytes > MaxBytes;
        _bytes += truncated ? HeaderBytes : wireBytes;
        var frame = new RunFrame(
            _frames.Count + 1,
            (long)_time.GetElapsedTime(_startedAt).TotalMilliseconds,
            e.TryGetProperty("type", out var type) ? type.GetString() ?? "" : "",
            null,
            wireBytes,
            null,
            truncated ? null : e.Clone(),
            truncated);
        _frames.Add(frame);
        return frame;
    }
}

/// <summary>
/// Where a run's frames are kept: beside the turn's trace, so they are read by whoever may read it and deleted
/// when it is. A run that recorded no turn has nowhere to put them, and does not try.
/// </summary>
public sealed class RunFrameStore(IDbContextFactory<MafDbContext> db, ILogger<RunFrameStore> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task SaveAsync(string? turnId, IReadOnlyList<RunFrame> frames, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(turnId) || frames.Count == 0)
        {
            return;
        }
        try
        {
            await using var ctx = await db.CreateDbContextAsync(ct);
            var json = JsonSerializer.Serialize(frames, Json);
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
