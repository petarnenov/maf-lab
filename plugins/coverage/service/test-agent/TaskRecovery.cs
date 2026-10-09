using A2A;
using Microsoft.Extensions.Options;

namespace Maf.Lab.TestAgent;

/// <summary>
/// Takes over the tasks a stopped process left running: at start, and then every <see cref="TestAgentOptions.RecoverEvery"/>.
/// A task is taken when it is submitted or working, no replica holds its lease, and this replica wins the lease. With a
/// checkpoint it is resumed; without one it ends <c>interrupted</c> — once it has been quiet longer than a lease, so a
/// task another replica is just accepting is left alone.
/// </summary>
public sealed class TaskRecovery(
    ITaskStore tasks,
    ITaskCheckpointStore checkpoints,
    TestGenerationHandler handler,
    IOptions<TestAgentOptions> options,
    TimeProvider time,
    ILogger<TaskRecovery> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.RecoverEvery, time);
        do
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("task recovery sweep failed ({ErrorType})", ex.GetType().Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task SweepAsync(CancellationToken ct)
    {
        var lease = options.Value.LeaseFor;
        var listed = await tasks.ListTasksAsync(new ListTasksRequest { PageSize = 200 }, ct);
        foreach (var task in listed.Tasks ?? [])
        {
            if (task.Status?.State is not (TaskState.Submitted or TaskState.Working) || handler.IsRunning(task.Id)
                || await checkpoints.IsLeasedAsync(task.Id, ct))
            {
                continue;
            }
            var checkpoint = await checkpoints.GetAsync(task.Id, ct);
            if (checkpoint is null && time.GetUtcNow() - task.Status.Timestamp < lease)
            {
                continue;
            }
            if (!await checkpoints.TakeLeaseAsync(task.Id, handler.Instance, lease, ct))
            {
                continue;
            }
            logger.LogInformation("taking over task={TaskId} checkpoint={HasCheckpoint}", task.Id, checkpoint is not null);
            var id = task.Id;
            _ = Task.Run(() => checkpoint is null ? handler.FailInterruptedAsync(id) : handler.ResumeAsync(id, checkpoint), CancellationToken.None);
        }
    }
}
