using System.Text.Json;
using A2A;
using Maf.Lab.A2A;
using Microsoft.Extensions.Options;
using NSubstitute;
using StackExchange.Redis;

namespace Maf.Lab.Tests;

/// <summary>
/// The task store the reviewer's replicas share, with Redis itself substituted: the keys it writes, the expiry it
/// asks for and the index it keeps are what make a task started on one replica visible on another.
/// </summary>
public sealed class RedisTaskStoreTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = A2AJsonUtilities.DefaultOptions;

    private readonly IDatabase _db = Substitute.For<IDatabase>();
    private readonly Dictionary<string, string> _tasks = new();
    private readonly SortedDictionary<string, double> _index = new();
    private readonly RedisTaskStore _store;

    public RedisTaskStoreTests()
    {
        var redis = Substitute.For<IConnectionMultiplexer>();
        redis.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(_db);

        // Each save is a minute later than the last, so "newest first" is a real order and not a tie.
        var now = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var time = Substitute.For<TimeProvider>();
        time.GetUtcNow().Returns(_ =>
        {
            var at = now;
            now = now.AddMinutes(1);
            return at;
        });

        _store = new RedisTaskStore(redis, time, Options.Create(new A2AOptions { StoreKeyspace = "compliance" }));

        // Just enough Redis to stand in for the shared volume: strings by task id, one sorted set as the index.
        _db.StringGetAsync(Arg.Any<RedisKey>()).Returns(call =>
            _tasks.TryGetValue(Id(call.Arg<RedisKey>()), out var json) ? (RedisValue)json : RedisValue.Null);
        _db.StringSetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<Expiration>()).Returns(call =>
        {
            _tasks[Id(call.Arg<RedisKey>())] = call.Arg<RedisValue>().ToString();
            return true;
        });
        _db.KeyDeleteAsync(Arg.Any<RedisKey>()).Returns(call => _tasks.Remove(Id(call.Arg<RedisKey>())));
        _db.SortedSetAddAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<double>()).Returns(call =>
        {
            _index[call.Arg<RedisValue>().ToString()] = call.Arg<double>();
            return true;
        });
        _db.SortedSetRemoveAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>()).Returns(call =>
            _index.Remove(call.Arg<RedisValue>().ToString()));
        _db.SortedSetRangeByRankAsync(Arg.Any<RedisKey>(), Arg.Any<long>(), Arg.Any<long>(), Arg.Any<Order>())
            .Returns(_ => _index.OrderByDescending(e => e.Value).Select(e => (RedisValue)e.Key).ToArray());
    }

    /// <summary>The task id is the last segment of its key, after the keyspace.</summary>
    private static string Id(RedisKey key) => key.ToString().Split(':')[^1];

    private static AgentTask Task(string id, TaskState state, string context = "ctx-1") => new()
    {
        Id = id,
        ContextId = context,
        Status = new global::A2A.TaskStatus { State = state, Timestamp = DateTimeOffset.UtcNow },
        History = [new Message { MessageId = "m1", Role = global::A2A.Role.User, Parts = [new Part { Text = "status of run 4417" }] }],
    };

    [Fact]
    public async Task A_task_is_saved_under_its_keyspace_with_seven_days_of_life_and_is_indexed()
    {
        var task = Task("t-1", TaskState.Working);

        await _store.SaveTaskAsync("t-1", task, Ct);

        await _db.Received(1).StringSetAsync(
            (RedisKey)"task:compliance:t-1", JsonSerializer.Serialize(task, Json), (Expiration)TimeSpan.FromDays(7));
        await _db.Received(1).SortedSetAddAsync(
            (RedisKey)"task:compliance:index", (RedisValue)"t-1",
            new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds());
    }

    [Fact]
    public async Task A_saved_task_is_read_back_by_another_replica()
    {
        var task = Task("t-2", TaskState.Completed, "ctx-9");
        task.Artifacts = [new Artifact { ArtifactId = "a-1", Name = "run", Parts = [new Part { Text = "completed" }] }];

        await _store.SaveTaskAsync("t-2", task, Ct);
        var read = await _store.GetTaskAsync("t-2", Ct);

        Assert.NotNull(read);
        Assert.Equal("ctx-9", read!.ContextId);
        Assert.Equal(TaskState.Completed, read.Status!.State);
        Assert.Single(read.History!);
        Assert.Equal("a-1", Assert.Single(read.Artifacts!).ArtifactId);
    }

    [Fact]
    public async Task A_task_that_is_not_in_the_store_is_null()
    {
        Assert.Null(await _store.GetTaskAsync("t-404", Ct));
    }

    [Fact]
    public async Task Deleting_a_task_removes_it_and_its_index_entry()
    {
        await _store.SaveTaskAsync("t-3", Task("t-3", TaskState.Working), Ct);
        await _store.SaveTaskAsync("t-4", Task("t-4", TaskState.Working), Ct);

        await _store.DeleteTaskAsync("t-3", Ct);

        Assert.Null(await _store.GetTaskAsync("t-3", Ct));
        await _db.Received(1).KeyDeleteAsync((RedisKey)"task:compliance:t-3");
        await _db.Received(1).SortedSetRemoveAsync((RedisKey)"task:compliance:index", (RedisValue)"t-3");
        var left = await _store.ListTasksAsync(new ListTasksRequest(), Ct);
        Assert.Equal(["t-4"], left.Tasks.Select(t => t.Id));
    }

    [Fact]
    public async Task Listing_is_newest_first_and_scoped_by_context_and_state()
    {
        await _store.SaveTaskAsync("t-5", Task("t-5", TaskState.Working, "ctx-a"), Ct);
        await _store.SaveTaskAsync("t-6", Task("t-6", TaskState.Completed, "ctx-a"), Ct);
        await _store.SaveTaskAsync("t-7", Task("t-7", TaskState.Working, "ctx-b"), Ct);

        var all = await _store.ListTasksAsync(new ListTasksRequest(), Ct);
        var byContext = await _store.ListTasksAsync(new ListTasksRequest { ContextId = "ctx-a" }, Ct);
        var byState = await _store.ListTasksAsync(new ListTasksRequest { Status = TaskState.Working }, Ct);
        var byBlankContext = await _store.ListTasksAsync(new ListTasksRequest { ContextId = " " }, Ct);

        Assert.Equal(["t-7", "t-6", "t-5"], all.Tasks.Select(t => t.Id));
        Assert.Equal(["t-6", "t-5"], byContext.Tasks.Select(t => t.Id));
        Assert.Equal(["t-7", "t-5"], byState.Tasks.Select(t => t.Id));
        Assert.Equal(3, byBlankContext.TotalSize);
        Assert.Equal(50, all.PageSize);
    }

    [Fact]
    public async Task An_index_entry_whose_task_expired_is_dropped_rather_than_failing_the_listing()
    {
        await _store.SaveTaskAsync("t-8", Task("t-8", TaskState.Working), Ct);
        await _store.SaveTaskAsync("t-9", Task("t-9", TaskState.Working), Ct);
        // The string key expires on its own schedule; the index still points at it.
        await _db.KeyDeleteAsync((RedisKey)"task:compliance:t-8");

        var listed = await _store.ListTasksAsync(new ListTasksRequest(), Ct);

        Assert.Equal(["t-9"], listed.Tasks.Select(t => t.Id));
        Assert.Equal(1, listed.TotalSize);
        await _db.Received(1).SortedSetRemoveAsync((RedisKey)"task:compliance:index", (RedisValue)"t-8");
    }

    [Fact]
    public async Task A_page_of_two_lists_only_the_two_newest_tasks()
    {
        await _store.SaveTaskAsync("t-10", Task("t-10", TaskState.Working), Ct);
        await _store.SaveTaskAsync("t-11", Task("t-11", TaskState.Working), Ct);
        await _store.SaveTaskAsync("t-12", Task("t-12", TaskState.Working), Ct);

        var page = await _store.ListTasksAsync(new ListTasksRequest { PageSize = 2 }, Ct);
        var tooBig = await _store.ListTasksAsync(new ListTasksRequest { PageSize = 500 }, Ct);

        Assert.Equal(["t-12", "t-11"], page.Tasks.Select(t => t.Id));
        Assert.Equal(2, page.PageSize);
        Assert.Equal(2, page.TotalSize);
        Assert.Equal(50, tooBig.PageSize);
        Assert.Equal(3, tooBig.TotalSize);
    }
}