using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Maf.Lab.Domain.SharedState;
using Maf.Lab.Hosting;
using Maf.Lab.Hosting.Stores;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Maf.Lab.IntegrationTests;

/// <summary>
/// The run-state store against a real Redis (introduce-plugins decision 2): the compare-and-set that keeps a terminal
/// outcome, the run-grace expiry every write keeps, and a run whose owner has no heartbeat closed on read.
/// </summary>
public sealed class RedisRunStateScriptTests : IAsyncLifetime
{
    private const string Image = "redis:8.8.3-alpine";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IContainer _redis = new ContainerBuilder(Image)
        .WithPortBinding(6379, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("redis-cli", "ping"))
        .Build();

    private ConnectionMultiplexer? _connection;
    private RedisRunStateStore _store = null!;

    public async ValueTask InitializeAsync()
    {
        await _redis.StartAsync(Ct);
        _connection = await ConnectionMultiplexer.ConnectAsync($"{_redis.Hostname}:{_redis.GetMappedPublicPort(6379)}");
        _store = new RedisRunStateStore(_connection, Options.Create(new SharedStateOptions { RunGrace = TimeSpan.FromMinutes(5) }));
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
        await _redis.DisposeAsync();
    }

    private static RunState State(string runId, string outcome, string? instance) =>
        new(runId, "c-1", "adam", "firm-a", "", [], outcome, null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, instance);

    [Theory]
    [InlineData(RunOutcomes.Running)]
    [InlineData(RunOutcomes.Answered)]
    public async Task A_cancelled_run_stays_cancelled(string late)
    {
        await _store.SaveAsync(State("r-1", RunOutcomes.Cancelled, null), Ct);

        await _store.SaveAsync(State("r-1", late, null), Ct);

        Assert.Equal(RunOutcomes.Cancelled, (await _store.GetAsync("r-1", Ct))!.Outcome);
    }

    [Fact]
    public async Task An_answered_run_is_not_undone_by_a_late_cancel()
    {
        await _store.SaveAsync(State("r-2", RunOutcomes.Answered, null), Ct);

        await _store.SaveAsync(State("r-2", RunOutcomes.Cancelled, null), Ct);

        Assert.Equal(RunOutcomes.Answered, (await _store.GetAsync("r-2", Ct))!.Outcome);
    }

    [Fact]
    public async Task Every_write_keeps_the_run_grace_expiry()
    {
        await _store.SaveAsync(State("r-3", RunOutcomes.Running, null), Ct);
        await _store.SaveAsync(State("r-3", RunOutcomes.Answered, null), Ct);

        var ttl = await _connection!.GetDatabase().KeyTimeToLiveAsync("run:r-3");
        Assert.NotNull(ttl);
        Assert.InRange(ttl!.Value, TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task A_running_run_whose_owner_has_no_heartbeat_is_cancelled_on_read()
    {
        await _store.SaveAsync(State("r-4", RunOutcomes.Running, "gone-process"), Ct);

        var read = await _store.GetAsync("r-4", Ct);

        Assert.Equal(RunOutcomes.Cancelled, read!.Outcome);
        Assert.Equal(RunOutcomes.Cancelled, (await _store.GetAsync("r-4", Ct))!.Outcome);
    }

    [Fact]
    public async Task A_running_run_whose_owner_beats_stays_running()
    {
        await _connection!.GetDatabase().StringSetAsync(RedisRunStateStore.HeartbeatKey("live-process"), "1", TimeSpan.FromSeconds(20));
        await _store.SaveAsync(State("r-5", RunOutcomes.Running, "live-process"), Ct);

        Assert.Equal(RunOutcomes.Running, (await _store.GetAsync("r-5", Ct))!.Outcome);
    }

    [Fact]
    public async Task A_legacy_run_without_an_owner_is_left_as_it_is()
    {
        await _store.SaveAsync(State("r-6", RunOutcomes.Running, null), Ct);

        Assert.Equal(RunOutcomes.Running, (await _store.GetAsync("r-6", Ct))!.Outcome);
    }

    [Fact]
    public void The_process_id_is_unique_per_start_and_starts_with_the_host_name()
    {
        Assert.StartsWith(Environment.MachineName + "-", InstanceIdentity.ProcessId);
        Assert.NotEqual(InstanceIdentity.Name, InstanceIdentity.ProcessId);
    }
}
