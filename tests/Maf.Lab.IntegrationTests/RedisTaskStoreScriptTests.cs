using A2A;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Maf.Lab.A2A;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Maf.Lab.IntegrationTests;

/// <summary>
/// The task store's save script against a real Redis (the image compose runs): a task that has ended is not written
/// over, however late the other replica's write is. The unit tests check the C# around it; only Redis runs the Lua.
/// </summary>
public sealed class RedisTaskStoreScriptTests : IAsyncLifetime
{
    private const string Image = "redis:8.8.3-alpine";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IContainer _redis = new ContainerBuilder(Image)
        .WithPortBinding(6379, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("redis-cli", "ping"))
        .Build();

    private ConnectionMultiplexer? _connection;
    private RedisTaskStore _store = null!;

    public async ValueTask InitializeAsync()
    {
        await _redis.StartAsync(Ct);
        _connection = await ConnectionMultiplexer.ConnectAsync($"{_redis.Hostname}:{_redis.GetMappedPublicPort(6379)}");
        _store = new RedisTaskStore(_connection, TimeProvider.System, Options.Create(new A2AOptions { StoreKeyspace = "compliance" }));
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
        await _redis.DisposeAsync();
    }

    private static AgentTask Task(string id, TaskState state) => new()
    {
        Id = id,
        ContextId = "ctx-1",
        Status = new global::A2A.TaskStatus { State = state, Timestamp = DateTimeOffset.UtcNow },
    };

    [Theory]
    [InlineData(TaskState.Working)]
    [InlineData(TaskState.Completed)]
    public async Task A_cancelled_task_stays_cancelled(TaskState late)
    {
        await _store.SaveTaskAsync("t-1", Task("t-1", TaskState.Canceled), Ct);

        await _store.SaveTaskAsync("t-1", Task("t-1", late), Ct);

        Assert.Equal(TaskState.Canceled, (await _store.GetTaskAsync("t-1", Ct))!.Status!.State);
    }

    [Fact]
    public async Task A_running_task_finishes_and_keeps_its_life_and_index()
    {
        await _store.SaveTaskAsync("t-2", Task("t-2", TaskState.Working), Ct);
        await _store.SaveTaskAsync("t-2", Task("t-2", TaskState.Completed), Ct);

        Assert.Equal(TaskState.Completed, (await _store.GetTaskAsync("t-2", Ct))!.Status!.State);
        var ttl = await _connection!.GetDatabase().KeyTimeToLiveAsync("task:compliance:t-2");
        Assert.InRange(ttl!.Value, TimeSpan.FromDays(6.9), TimeSpan.FromDays(7));
        var listed = await _store.ListTasksAsync(new ListTasksRequest(), Ct);
        Assert.Contains(listed.Tasks!, t => t.Id == "t-2");
    }

    [Fact]
    public async Task The_same_end_saved_again_is_kept()
    {
        // The SDK saves a finished task more than once (its last message, then its artifacts): that must not be refused.
        var done = Task("t-3", TaskState.Completed);
        await _store.SaveTaskAsync("t-3", done, Ct);
        done.Artifacts = [new Artifact { ArtifactId = "a-1", Parts = [new Part { Text = "verdict" }] }];

        await _store.SaveTaskAsync("t-3", done, Ct);

        Assert.Single((await _store.GetTaskAsync("t-3", Ct))!.Artifacts!);
    }
}
