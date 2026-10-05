using A2A;

namespace Maf.Lab.A2A;

/// <summary>
/// Watches the shared store while a task runs, and cancels <see cref="Token"/> once the store says the task is
/// canceled. A cancel may reach any replica; the one doing the work learns of it here, not from the request that
/// carried it — so a cancel needs no routing to where the task runs. A read that fails is tried again on the next tick:
/// the watch never ends the work by itself.
/// </summary>
public sealed class TaskCancelWatch : IAsyncDisposable
{
    private readonly CancellationTokenSource _canceled = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    private TaskCancelWatch(ITaskStore store, string taskId, TimeSpan every, TimeProvider time)
    {
        _loop = WatchAsync(store, taskId, every, time);
    }

    /// <summary>Cancelled once the store records the task as canceled.</summary>
    public CancellationToken Token => _canceled.Token;

    /// <summary>Whether the store has said the task is canceled.</summary>
    public bool Canceled => _canceled.IsCancellationRequested;

    public static TaskCancelWatch Start(ITaskStore store, string taskId, TimeSpan every, TimeProvider? time = null) =>
        new(store, taskId, every, time ?? TimeProvider.System);

    private async Task WatchAsync(ITaskStore store, string taskId, TimeSpan every, TimeProvider time)
    {
        using var timer = new PeriodicTimer(every, time);
        try
        {
            while (await timer.WaitForNextTickAsync(_stop.Token))
            {
                AgentTask? task;
                try
                {
                    task = await store.GetTaskAsync(taskId, _stop.Token);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    continue;
                }
                if (task?.Status?.State == TaskState.Canceled)
                {
                    await _canceled.CancelAsync();
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await _loop;
        _stop.Dispose();
        _canceled.Dispose();
    }
}
