using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Maf.Lab.Api.Plugins;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;

namespace Maf.Lab.IntegrationTests;

public sealed class TenantPluginReplicaTests : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly IContainer _redis = new ContainerBuilder("redis:8.8.3-alpine").WithPortBinding(6379, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("redis-cli", "ping")).Build();
    private ConnectionMultiplexer _a = null!;
    private ConnectionMultiplexer _b = null!;

    public async ValueTask InitializeAsync()
    {
        await _redis.StartAsync(Ct);
        var endpoint = $"{_redis.Hostname}:{_redis.GetMappedPublicPort(6379)}";
        _a = await ConnectionMultiplexer.ConnectAsync(endpoint);
        _b = await ConnectionMultiplexer.ConnectAsync(endpoint);
    }
    public async ValueTask DisposeAsync()
    {
        if (_a is not null) await _a.DisposeAsync();
        if (_b is not null) await _b.DisposeAsync();
        await _redis.DisposeAsync();
    }

    [Fact]
    public async Task A_switch_commits_once_and_the_other_replica_drops_its_cached_set()
    {
        await using var f = new TenantPluginFixture();
        await f.InitializeAsync(Ct);
        var busA = new PluginAccessChanges(NullLogger<PluginAccessChanges>.Instance, _a);
        var busB = new PluginAccessChanges(NullLogger<PluginAccessChanges>.Instance, _b);
        await busA.StartAsync(Ct);
        await busB.StartAsync(Ct);
        try
        {
            var storeA = f.Store(busA);
            var storeB = f.Store(busB);
            var accessB = f.Access(storeB, busB);
            Assert.False((await accessB.For(TenantPluginFixture.User, Ct)).IsInUse("weather"));
            var noticed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var subscription = busB.Subscribe(_ => noticed.TrySetResult());
            await storeA.AllowAsync(TenantPluginFixture.Operator, "weather", true, true, Ct);
            await noticed.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
            Assert.True((await accessB.For(TenantPluginFixture.User, Ct)).IsInUse("weather"));
            Assert.Equal(await storeA.ReadAsync(TenantPluginFixture.Admin, Ct), await storeB.ReadAsync(TenantPluginFixture.Admin, Ct));
        }
        finally { await busA.StopAsync(Ct); await busB.StopAsync(Ct); }
    }
}
