using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Maf.Lab.Hosting.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using StackExchange.Redis;

namespace Maf.Lab.Tests;

/// <summary>
/// A host that stops ends its background work before it disposes the services that work uses (stop-anything): nothing
/// it started is left writing through a disposed database or client.
/// </summary>
public sealed class HostShutdownTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_disposed_heartbeat_waits_for_the_beat_in_flight()
    {
        var beat = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var beats = 0;
        var store = Substitute.For<IDatabase>();
        store.StringSetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), RunOwnerHeartbeat.Ttl).Returns(_ =>
        {
            Interlocked.Increment(ref beats);
            started.TrySetResult();
            return beat.Task;
        });
        var redis = Substitute.For<IConnectionMultiplexer>();
        redis.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(store);
        using var stopped = new CancellationTokenSource();
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        lifetime.ApplicationStopped.Returns(stopped.Token);
        var heartbeat = new RunOwnerHeartbeat(redis, lifetime);
        await heartbeat.StartAsync(Ct);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        await stopped.CancelAsync();
        var disposing = heartbeat.DisposeAsync().AsTask();
        // The beat has not answered: disposing is still waiting on it, so the multiplexer is not yet disposed under it.
        Assert.False(disposing.IsCompleted);

        beat.SetResult(true);
        await disposing.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Assert.Equal(1, Volatile.Read(ref beats));
    }

}
