using System.Text.Json;
using Maf.Lab.Domain.SharedState;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Maf.Lab.Hosting.Stores;

/// <summary>
/// What has already been answered under a caller's idempotency key. The protocol says an interrupted stream is
/// sent again; this is what makes sending it again safe.
/// </summary>
public sealed class RedisIdempotencyStore(IConnectionMultiplexer redis, IOptions<SharedStateOptions> options, TimeProvider time)
    : IIdempotencyStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Keyed by firm as well, so one firm's key can never answer another's call.</summary>
    private static RedisKey Key(string tenantId, string key) => $"idem:{tenantId}:{key}";

    public async Task<(IdempotencyOutcome Outcome, IdempotentAnswer? Answer)> CheckAsync(
        string tenantId, string key, string requestDigest, CancellationToken ct)
    {
        var value = await redis.GetDatabase().StringGetAsync(Key(tenantId, key));
        if (value.IsNullOrEmpty)
        {
            return (IdempotencyOutcome.Fresh, null);
        }
        var recorded = JsonSerializer.Deserialize<IdempotentAnswer>((string)value!, Json);
        if (recorded is null)
        {
            return (IdempotencyOutcome.Fresh, null);
        }
        // The same key for a different call is a mistake, not a retry: answering it with the first answer would
        // be answering the wrong question.
        return recorded.RequestDigest == requestDigest
            ? (IdempotencyOutcome.Replay, recorded)
            : (IdempotencyOutcome.Conflict, recorded);
    }

    public async Task RecordAsync(string tenantId, string key, string requestDigest, string answer, CancellationToken ct)
    {
        var record = new IdempotentAnswer(key, requestDigest, answer, time.GetUtcNow());
        await redis.GetDatabase().StringSetAsync(
            Key(tenantId, key), JsonSerializer.Serialize(record, Json), options.Value.IdempotencyWindow);
    }
}
