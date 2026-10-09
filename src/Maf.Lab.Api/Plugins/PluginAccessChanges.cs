using Maf.Lab.Domain.Tenancy;
using StackExchange.Redis;

namespace Maf.Lab.Api.Plugins;

/// <summary>Cache invalidation: local delivery plus the deployment's Redis pub/sub channel.</summary>
public interface IPluginAccessChanges
{
    IDisposable Subscribe(Action<TenantId> changed);
    Task PublishAsync(TenantId changedTenant);
}

public sealed class PluginAccessChanges(ILogger<PluginAccessChanges> logger, IConnectionMultiplexer? redis = null)
    : IPluginAccessChanges, IHostedService
{
    public const string Channel = "tenant-plugins-changed";
    private readonly object _gate = new();
    private readonly List<Action<TenantId>> _handlers = [];
    private Action<RedisChannel, RedisValue>? _redisHandler;

    public IDisposable Subscribe(Action<TenantId> changed)
    {
        lock (_gate) _handlers.Add(changed);
        return new Subscription(() => { lock (_gate) _handlers.Remove(changed); });
    }

    private void Dispatch(TenantId changedTenant)
    {
        Action<TenantId>[] subscribers;
        lock (_gate) subscribers = [.. _handlers];
        foreach (var subscriber in subscribers) subscriber(changedTenant);
    }

    public async Task PublishAsync(TenantId changedTenant)
    {
        Dispatch(changedTenant);
        if (redis is null) return;
        try { await redis.GetSubscriber().PublishAsync(RedisChannel.Literal(Channel), changedTenant.Value); }
        catch (Exception ex)
        {
            // The committed write stands; other replicas discard stale entries within their fixed TTL.
            logger.LogWarning("plugin access invalidation delivery failed ({ErrorType})", ex.GetType().Name);
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (redis is null) return;
        _redisHandler = (_, value) =>
        {
            if (TenantId.TryParse(value.ToString(), out var parsed) && !parsed.IsShared) Dispatch(parsed);
        };
        await redis.GetSubscriber().SubscribeAsync(RedisChannel.Literal(Channel), _redisHandler);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (redis is not null && _redisHandler is not null)
            await redis.GetSubscriber().UnsubscribeAsync(RedisChannel.Literal(Channel), _redisHandler);
    }

    private sealed class Subscription(Action unsubscribe) : IDisposable
    {
        private Action? _unsubscribe = unsubscribe;
        public void Dispose() => Interlocked.Exchange(ref _unsubscribe, null)?.Invoke();
    }
}
