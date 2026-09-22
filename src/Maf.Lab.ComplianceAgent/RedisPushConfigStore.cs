using System.Text.Json;
using Maf.Lab.A2A;
using StackExchange.Redis;

namespace Maf.Lab.ComplianceAgent;

/// <summary>
/// The webhooks a caller registered for a review, in the store the reviewer's replicas share — so the POST from
/// a partner reaches a replica that knows about the webhook, not only the one that took the registration.
/// </summary>
public sealed class RedisPushConfigStore(IConnectionMultiplexer redis) : IPushConfigStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>With the task, because a webhook outliving the review it watches is a webhook nobody wanted.</summary>
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    private static RedisKey Key(string taskId) => $"push:compliance:{taskId}";

    public async Task SaveAsync(PushConfigRecord config, CancellationToken ct)
    {
        var database = redis.GetDatabase();
        await database.HashSetAsync(Key(config.TaskId), config.Id, JsonSerializer.Serialize(config, Json));
        await database.KeyExpireAsync(Key(config.TaskId), Retention);
    }

    public async Task<IReadOnlyList<PushConfigRecord>> ListAsync(string taskId, CancellationToken ct)
    {
        var entries = await redis.GetDatabase().HashGetAllAsync(Key(taskId));
        return [.. entries
            .Select(e => JsonSerializer.Deserialize<PushConfigRecord>((string)e.Value!, Json))
            .Where(c => c is not null)
            .Select(c => c!)];
    }

    public async Task DeleteAsync(string taskId, string id, CancellationToken ct) =>
        await redis.GetDatabase().HashDeleteAsync(Key(taskId), id);
}
