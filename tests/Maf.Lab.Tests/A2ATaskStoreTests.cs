using A2A;
using Maf.Lab.Api.A2A;
using Maf.Lab.Api.Storage;
using Maf.Lab.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Tests;

/// <summary>
/// A task must outlive the process that started it: the SDK's own store is per-process, and two replicas behind a
/// balancer would each see half the truth.
/// </summary>
public class A2ATaskStoreTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ITaskStore Store(ApiFactory api) => api.Services.GetRequiredService<ITaskStore>();

    private static AgentTask Task(string id, TaskState state, string context = "ctx-1") => new()
    {
        Id = id,
        ContextId = context,
        Status = new A2A.TaskStatus { State = state, Timestamp = DateTimeOffset.UtcNow },
        History = [new Message { MessageId = "m1", Role = Role.User, Parts = [new Part { Text = "status of run 4417" }] }],
    };

    [Fact]
    public async Task A_task_round_trips_with_its_history_and_artifacts()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var store = Store(api);
        var task = Task("t-1", TaskState.Working);
        task.Artifacts = [new Artifact { ArtifactId = "a-1", Name = "run", Parts = [new Part { Text = "completed" }] }];

        await store.SaveTaskAsync(task.Id, task, Ct);
        var read = await store.GetTaskAsync("t-1", Ct);

        Assert.NotNull(read);
        Assert.Equal(TaskState.Working, read!.Status!.State);
        Assert.Equal("ctx-1", read.ContextId);
        Assert.Single(read.History!);
        Assert.Equal("a-1", Assert.Single(read.Artifacts!).ArtifactId);
    }

    [Fact]
    public async Task Another_replica_sees_the_same_task()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        // Two stores over one database stand in for two replicas over one volume.
        var first = Store(api);
        var second = new SqliteTaskStore(
            api.Services.GetRequiredService<IDbContextFactory<MafDbContext>>(),
            TimeProvider.System,
            api.Services.GetRequiredService<PushNotificationDispatcher>());

        await first.SaveTaskAsync("t-2", Task("t-2", TaskState.Submitted), Ct);
        var seenBySecond = await second.GetTaskAsync("t-2", Ct);
        await second.SaveTaskAsync("t-2", Task("t-2", TaskState.Completed), Ct);
        var seenByFirst = await first.GetTaskAsync("t-2", Ct);

        Assert.Equal(TaskState.Submitted, seenBySecond!.Status!.State);
        Assert.Equal(TaskState.Completed, seenByFirst!.Status!.State);
    }

    [Fact]
    public async Task Transitions_are_not_lost_when_they_arrive_together()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var store = Store(api);
        await store.SaveTaskAsync("t-3", Task("t-3", TaskState.Submitted), Ct);

        // Concurrent writers to the same task: the last state must be one of them, and the row must survive.
        await System.Threading.Tasks.Task.WhenAll(
            Enumerable.Range(0, 8).Select(i =>
                store.SaveTaskAsync("t-3", Task("t-3", i % 2 == 0 ? TaskState.Working : TaskState.Completed), Ct)));

        var read = await store.GetTaskAsync("t-3", Ct);
        Assert.NotNull(read);
        Assert.Contains(read!.Status!.State, new[] { TaskState.Working, TaskState.Completed });

        await using var db = ChatApiTests.Db(api);
        Assert.Equal(1, await db.A2ATasks.CountAsync(t => t.Id == "t-3", Ct));
    }

    [Fact]
    public async Task Listing_is_scoped_by_context_and_state()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var store = Store(api);
        await store.SaveTaskAsync("t-4", Task("t-4", TaskState.Working, "ctx-a"), Ct);
        await store.SaveTaskAsync("t-5", Task("t-5", TaskState.Completed, "ctx-a"), Ct);
        await store.SaveTaskAsync("t-6", Task("t-6", TaskState.Working, "ctx-b"), Ct);

        var byContext = await store.ListTasksAsync(new ListTasksRequest { ContextId = "ctx-a" }, Ct);
        var byState = await store.ListTasksAsync(new ListTasksRequest { Status = TaskState.Working }, Ct);

        Assert.Equal(["t-5", "t-4"], byContext.Tasks.Select(t => t.Id).Order().Reverse());
        Assert.Equal(["t-4", "t-6"], byState.Tasks.Select(t => t.Id).Order());
    }

    [Fact]
    public async Task Deleting_a_task_takes_its_push_configuration_with_it()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var store = Store(api);
        await store.SaveTaskAsync("t-7", Task("t-7", TaskState.Working), Ct);
        await using (var db = ChatApiTests.Db(api))
        {
            db.A2APushConfigs.Add(new A2APushConfigRow
            {
                Id = "p-1", TaskId = "t-7", Url = "http://localhost/hook", Token = "tok", CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(Ct);
        }

        await store.DeleteTaskAsync("t-7", Ct);

        Assert.Null(await store.GetTaskAsync("t-7", Ct));
        await using var after = ChatApiTests.Db(api);
        Assert.Empty(await after.A2APushConfigs.Where(c => c.TaskId == "t-7").ToListAsync(Ct));
    }
}
