using A2A;

namespace Maf.Lab.Tests;

/// <summary>
/// The SDK's in-memory store with the rule the shared stores keep (stop-anything): a task that has ended is not written
/// over with another state. Several hosts in one test share it, as replicas share Redis — whose own guard, the save
/// script, is tested against a real Redis in the integration tests.
/// </summary>
public sealed class TerminalGuardTaskStore : ITaskStore
{
    private static readonly TaskState[] Terminal = [TaskState.Completed, TaskState.Canceled, TaskState.Failed, TaskState.Rejected];
    private readonly InMemoryTaskStore inner = new();
    private readonly SemaphoreSlim gate = new(1, 1);

    public Task<AgentTask?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default) =>
        inner.GetTaskAsync(taskId, cancellationToken);

    public async Task SaveTaskAsync(string taskId, AgentTask task, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var stored = (await inner.GetTaskAsync(taskId, cancellationToken))?.Status?.State;
            if (stored is { } state && Terminal.Contains(state) && task.Status?.State != state)
            {
                return;
            }
            await inner.SaveTaskAsync(taskId, task, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public Task DeleteTaskAsync(string taskId, CancellationToken cancellationToken = default) =>
        inner.DeleteTaskAsync(taskId, cancellationToken);

    public Task<ListTasksResponse> ListTasksAsync(ListTasksRequest request, CancellationToken cancellationToken = default) =>
        inner.ListTasksAsync(request, cancellationToken);
}
