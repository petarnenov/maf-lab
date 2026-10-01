using System.Text.Json;
using Maf.Lab.Domain.SharedState;
using Maf.Lab.Domain.Tracing;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Maf.Lab.Hosting.Stores;

/// <summary>
/// A run's trace as it is written, one list entry per event, so the monitor can follow a live run from any replica.
/// Kept for the same grace period as the run's snapshot.
/// </summary>
public sealed class RedisRunTraceStore(IConnectionMultiplexer redis, IOptions<SharedStateOptions> options) : IRunTraceStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static RedisKey Key(string runId) => $"runtrace:{runId}";

    public async Task AppendAsync(string runId, TraceEvent traceEvent, CancellationToken ct)
    {
        var db = redis.GetDatabase();
        await db.ListRightPushAsync(Key(runId), JsonSerializer.Serialize(traceEvent, Json));
        await db.KeyExpireAsync(Key(runId), options.Value.RunGrace);
    }

    public async Task<IReadOnlyList<TraceEvent>> ReadAsync(string runId, int afterSeq, CancellationToken ct)
    {
        // Sequence numbers start at 1 and are appended in order, so the events after seq n start at index n.
        var values = await redis.GetDatabase().ListRangeAsync(Key(runId), Math.Max(0, afterSeq));
        return [.. values.Where(v => !v.IsNullOrEmpty).Select(v => JsonSerializer.Deserialize<TraceEvent>((string)v!, Json)!)];
    }
}
