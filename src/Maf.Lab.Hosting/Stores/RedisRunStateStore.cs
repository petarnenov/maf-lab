using System.Text.Json;
using Maf.Lab.Domain.SharedState;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Maf.Lab.Hosting.Stores;

/// <summary>
/// A run's snapshot in the shared store, so a client that closed its tab can come back on any replica. Kept for
/// the run and a grace period after it: long enough to come back to, short enough not to be a second history.
/// </summary>
public sealed class RedisRunStateStore(IConnectionMultiplexer redis, IOptions<SharedStateOptions> options) : IRunStateStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static RedisKey Key(string runId) => $"run:{runId}";

    public async Task SaveAsync(RunState state, CancellationToken ct)
    {
        var value = JsonSerializer.Serialize(state, Json);
        // The grace period starts again on every write, so a long run is not forgotten while it is still running.
        await redis.GetDatabase().StringSetAsync(Key(state.RunId), value, options.Value.RunGrace);
    }

    public async Task<RunState?> GetAsync(string runId, CancellationToken ct)
    {
        var value = await redis.GetDatabase().StringGetAsync(Key(runId));
        return value.IsNullOrEmpty ? null : JsonSerializer.Deserialize<RunState>((string)value!, Json);
    }
}
