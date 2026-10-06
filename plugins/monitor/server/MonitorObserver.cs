using System.Collections.Concurrent;
using System.Text.Json;
using Maf.Lab.Domain.SharedState;
using Maf.Lab.Domain.Tracing;
using Maf.Lab.Plugins.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Maf.Lab.Plugins.Monitor;

/// <summary>
/// The monitor's turn observer (introduce-plugins decision 7): it wants every event and the run's frames. Each event is
/// appended to the run's live trace in the shared store, so the monitor reads it from any replica while the turn runs;
/// at turn.end the whole trace is kept in the monitor's table, and the frames join it when the run's response ends.
/// A singleton: it keys what it holds by the run id and lets go of it at turn.end.
/// </summary>
public sealed class MonitorObserver(IRunTraceStore live, IDbContextFactory<DbContext> db, TimeProvider time, ILogger<MonitorObserver> logger)
    : ITurnObserver
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentDictionary<string, List<TraceEvent>> _runs = new(StringComparer.Ordinal);

    public bool IsEnabled(string kind) => true;

    public async Task OnEventAsync(string runId, TraceEvent e, CancellationToken ct)
    {
        try
        {
            await live.AppendAsync(runId, e, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The stored trace is still kept at turn.end; only the live view misses this event.
            logger.LogWarning("live trace append failed ({ErrorType})", ex.GetType().Name);
        }
        var events = _runs.GetOrAdd(runId, _ => []);
        events.Add(e);
        if (e.Kind == TraceKinds.TurnEnd && _runs.TryRemove(runId, out var turn))
        {
            await KeepAsync(turn, ct);
        }
    }

    public async Task OnFramesAsync(string runId, string? turnId, IReadOnlyList<RunFrame> frames, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(turnId) || frames.Count == 0)
        {
            return;
        }
        try
        {
            await using var ctx = await db.CreateDbContextAsync(ct);
            var json = JsonSerializer.Serialize(frames, Json);
            await ctx.Set<TurnDiagnosticsRow>().Where(t => t.TurnId == turnId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.AguiJson, json), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The frames are a view of a turn, not the turn: losing them never fails the run that just ended.
            logger.LogWarning("could not store run frames for turn {TurnId}: {ErrorType}", turnId, ex.GetType().Name);
        }
    }

    /// <summary>The turn's trace, under the identity its turn.start names, in the monitor's table.</summary>
    private async Task KeepAsync(List<TraceEvent> events, CancellationToken ct)
    {
        var start = events.FirstOrDefault(e => e.Kind == TraceKinds.TurnStart)?.Data;
        if (start is not { ValueKind: JsonValueKind.Object } s
            || Str(s, "turnId") is not { } turnId || Str(s, "conversationId") is not { } conversationId
            || !s.TryGetProperty("principal", out var principal) || Str(principal, "userId") is not { } userId
            || Str(principal, "tenantId") is not { } tenantId)
        {
            return;
        }
        try
        {
            await using var ctx = await db.CreateDbContextAsync(ct);
            ctx.Set<TurnDiagnosticsRow>().Add(new TurnDiagnosticsRow
            {
                TurnId = turnId,
                ConversationId = conversationId,
                UserId = userId,
                TenantId = tenantId,
                CreatedAt = time.GetUtcNow().UtcDateTime,
                Json = JsonSerializer.Serialize(events, Json),
            });
            await ctx.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("could not keep the trace of turn {TurnId}: {ErrorType}", turnId, ex.GetType().Name);
        }
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
