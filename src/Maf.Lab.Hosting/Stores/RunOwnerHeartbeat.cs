using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace Maf.Lab.Hosting.Stores;

/// <summary>
/// This process's heartbeat in the shared store (introduce-plugins decision 2): written every
/// <see cref="Interval"/> with a TTL of <see cref="Ttl"/>, from start until the host has fully stopped. It keeps going
/// through a graceful drain, so a run that is still finishing on a stopping replica is never taken for orphaned; only
/// when the process is gone does the key expire, and a reader then closes the process's runs.
/// </summary>
public sealed class RunOwnerHeartbeat(IConnectionMultiplexer redis, IHostApplicationLifetime lifetime) : IHostedService, IDisposable
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(20);

    private readonly CancellationTokenSource _stopped = new();
    private Task? _loop;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Not the host's stopping token: that fires when shutdown begins, and the drain is still running then.
        lifetime.ApplicationStopped.Register(() => _stopped.Cancel());
        _loop = Task.Run(() => BeatAsync(_stopped.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    private async Task BeatAsync(CancellationToken ct)
    {
        var key = RedisRunStateStore.HeartbeatKey(InstanceIdentity.ProcessId);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await redis.GetDatabase().StringSetAsync(key, "1", Ttl);
            }
            catch (RedisException)
            {
                // The store is down; the next beat tries again. Health already reports the store.
            }
            try
            {
                await Task.Delay(Interval, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose()
    {
        _stopped.Cancel();
        _stopped.Dispose();
    }
}
