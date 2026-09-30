using System.Text.Json;
using Maf.Lab.A2A;
using Microsoft.Extensions.Options;
using NSubstitute;
using StackExchange.Redis;

namespace Maf.Lab.Tests;

/// <summary>
/// The shared webhook store over Redis, with Redis itself substituted: the calls it makes are what the replicas
/// share, so they are what is checked (add-mocking-library).
/// </summary>
public sealed class RedisPushConfigStoreTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IDatabase _db = Substitute.For<IDatabase>();
    private readonly RedisPushConfigStore _store;

    public RedisPushConfigStoreTests()
    {
        var redis = Substitute.For<IConnectionMultiplexer>();
        redis.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(_db);
        _store = new RedisPushConfigStore(redis, Options.Create(new A2AOptions { StoreKeyspace = "compliance" }));
    }

    [Fact]
    public async Task A_webhook_is_saved_under_its_task_and_expires_with_it()
    {
        var config = new PushConfigRecord("p1", "t1", "https://partner.test/hook", "secret-token");

        await _store.SaveAsync(config, Ct);

        await _db.Received(1).HashSetAsync("push:compliance:t1", "p1", JsonSerializer.Serialize(config, Json));
        await _db.Received(1).KeyExpireAsync("push:compliance:t1", TimeSpan.FromDays(7));
    }

    [Fact]
    public async Task A_tasks_webhooks_are_read_back_and_empty_entries_are_skipped()
    {
        var one = new PushConfigRecord("p1", "t1", "https://a.test/hook", null);
        var two = new PushConfigRecord("p2", "t1", "https://b.test/hook", "tok");
        _db.HashGetAllAsync("push:compliance:t1").Returns(
        [
            new HashEntry("p1", JsonSerializer.Serialize(one, Json)),
            new HashEntry("gone", "null"),
            new HashEntry("p2", JsonSerializer.Serialize(two, Json)),
        ]);

        var listed = await _store.ListAsync("t1", Ct);

        Assert.Equal([one, two], listed);
    }

    [Fact]
    public async Task A_task_without_webhooks_lists_none()
    {
        _db.HashGetAllAsync("push:compliance:t9").Returns([]);

        Assert.Empty(await _store.ListAsync("t9", Ct));
    }

    [Fact]
    public async Task Deleting_removes_only_that_webhook()
    {
        await _store.DeleteAsync("t1", "p2", Ct);

        await _db.Received(1).HashDeleteAsync("push:compliance:t1", "p2");
        await _db.DidNotReceiveWithAnyArgs().KeyDeleteAsync(default(RedisKey));
    }
}
