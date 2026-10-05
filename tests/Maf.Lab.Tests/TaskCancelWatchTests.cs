using A2A;
using Maf.Lab.A2A;

namespace Maf.Lab.Tests;

/// <summary>The watch a running task keeps on the shared store: a cancel recorded there, by any replica, stops it.</summary>
public class TaskCancelWatchTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly TimeSpan Every = TimeSpan.FromMilliseconds(20);

    private static async Task<InMemoryTaskStore> StoreWithAsync(TaskState state)
    {
        var store = new InMemoryTaskStore();
        await store.SaveTaskAsync("t-1", new AgentTask
        {
            Id = "t-1",
            ContextId = "ctx",
            Status = new global::A2A.TaskStatus { State = state, Timestamp = DateTimeOffset.UtcNow },
        }, Ct);
        return store;
    }

    [Fact]
    public async Task A_cancel_recorded_in_the_store_cancels_the_token()
    {
        var store = await StoreWithAsync(TaskState.Working);
        await using var watch = TaskCancelWatch.Start(store, "t-1", Every);

        var task = await store.GetTaskAsync("t-1", Ct);
        task!.Status = new global::A2A.TaskStatus { State = TaskState.Canceled, Timestamp = DateTimeOffset.UtcNow };
        await store.SaveTaskAsync("t-1", task, Ct);

        await Task.Delay(Timeout.Infinite, watch.Token).ContinueWith(_ => { }, Ct).WaitAsync(TimeSpan.FromSeconds(5), Ct);
        Assert.True(watch.Canceled);
    }

    [Theory]
    [InlineData(TaskState.Working)]
    [InlineData(TaskState.Completed)]
    public async Task Any_other_state_leaves_the_work_alone(TaskState state)
    {
        var store = await StoreWithAsync(state);
        await using var watch = TaskCancelWatch.Start(store, "t-1", Every);

        await Task.Delay(200, Ct);

        Assert.False(watch.Canceled);
    }

    [Fact]
    public async Task Disposed_it_stops_reading()
    {
        var store = new CountingStore(await StoreWithAsync(TaskState.Working));
        var watch = TaskCancelWatch.Start(store, "t-1", Every);
        await Task.Delay(100, Ct);

        await watch.DisposeAsync();
        var reads = store.Reads;
        await Task.Delay(150, Ct);

        Assert.True(reads > 0);
        Assert.Equal(reads, store.Reads);
    }

    private sealed class CountingStore(ITaskStore inner) : ITaskStore
    {
        private int reads;
        public int Reads => Volatile.Read(ref reads);

        public Task<AgentTask?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref reads);
            return inner.GetTaskAsync(taskId, cancellationToken);
        }

        public Task SaveTaskAsync(string taskId, AgentTask task, CancellationToken cancellationToken = default) =>
            inner.SaveTaskAsync(taskId, task, cancellationToken);

        public Task DeleteTaskAsync(string taskId, CancellationToken cancellationToken = default) =>
            inner.DeleteTaskAsync(taskId, cancellationToken);

        public Task<ListTasksResponse> ListTasksAsync(ListTasksRequest request, CancellationToken cancellationToken = default) =>
            inner.ListTasksAsync(request, cancellationToken);
    }
}
