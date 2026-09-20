using A2A;
using Maf.Lab.Api.Storage;
using Maf.Lab.Retrieval.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Api.A2A;

/// <summary>
/// The SDK's <see cref="A2AServer"/> does the protocol work, except for five operations that throw in
/// 1.0.0-preview2: the four push-notification configuration calls and the extended agent card. They are declared
/// on <see cref="IA2ARequestHandler"/> and advertised in our card, so they are implemented here rather than
/// quietly dropped. Everything else is delegated untouched.
///
/// The gap is recorded in DECISIONS.md; when a later preview implements them, this class shrinks.
/// </summary>
public sealed class A2ARequestHandlerWithExtras(
    A2AServer inner,
    ITaskStore tasks,
    IDbContextFactory<MafDbContext> db,
    IPartnerAccessor partners,
    IOptions<A2AOptions> a2a,
    IOptions<AuthOptions> auth,
    TimeProvider time) : IA2ARequestHandler
{
    public Task<SendMessageResponse> SendMessageAsync(SendMessageRequest request, CancellationToken cancellationToken = default) =>
        inner.SendMessageAsync(request, cancellationToken);

    public IAsyncEnumerable<StreamResponse> SendStreamingMessageAsync(SendMessageRequest request, CancellationToken cancellationToken = default) =>
        inner.SendStreamingMessageAsync(request, cancellationToken);

    public Task<AgentTask> GetTaskAsync(GetTaskRequest request, CancellationToken cancellationToken = default) =>
        inner.GetTaskAsync(request, cancellationToken);

    public Task<ListTasksResponse> ListTasksAsync(ListTasksRequest request, CancellationToken cancellationToken = default) =>
        inner.ListTasksAsync(request, cancellationToken);

    public Task<AgentTask> CancelTaskAsync(CancelTaskRequest request, CancellationToken cancellationToken = default) =>
        inner.CancelTaskAsync(request, cancellationToken);

    /// <summary>
    /// Resubscription reads the shared task store rather than the SDK's in-process event channel. A caller that
    /// reconnects is balanced onto whichever replica is free — usually not the one running its task — and that
    /// replica has no channel to attach to: the stream would deliver the task and then hang until the caller gave
    /// up. The store is what both replicas share, so it is what a resubscription follows.
    ///
    /// The first event is the task as it stands, exactly as the specification requires, and the caller then
    /// receives every further change until the task is done.
    /// </summary>
    public async IAsyncEnumerable<StreamResponse> SubscribeToTaskAsync(SubscribeToTaskRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var taskId = request.Id ?? throw new A2AException("A task id is required.", A2AErrorCode.InvalidParams);
        var task = await tasks.GetTaskAsync(taskId, cancellationToken)
            ?? throw new A2AException($"Task {taskId} not found.", A2AErrorCode.TaskNotFound);

        yield return new StreamResponse { Task = task };
        if (IsTerminal(task.Status?.State))
        {
            yield break;
        }

        var seenStatus = Fingerprint(task.Status);
        var seenArtifacts = new HashSet<string>(task.Artifacts?.Select(a => a.ArtifactId ?? "") ?? []);
        var deadline = time.GetUtcNow() + a2a.Value.SubscribeTimeout;
        while (time.GetUtcNow() < deadline)
        {
            await System.Threading.Tasks.Task.Delay(a2a.Value.SubscribePollMs, cancellationToken);
            var current = await tasks.GetTaskAsync(taskId, cancellationToken);
            if (current is null)
            {
                yield break;
            }

            foreach (var artifact in current.Artifacts ?? [])
            {
                if (seenArtifacts.Add(artifact.ArtifactId ?? ""))
                {
                    yield return new StreamResponse
                    {
                        ArtifactUpdate = new TaskArtifactUpdateEvent
                        {
                            TaskId = taskId, ContextId = current.ContextId, Artifact = artifact, LastChunk = true,
                        },
                    };
                }
            }

            if (Fingerprint(current.Status) != seenStatus)
            {
                seenStatus = Fingerprint(current.Status);
                yield return new StreamResponse
                {
                    StatusUpdate = new TaskStatusUpdateEvent
                    {
                        TaskId = taskId, ContextId = current.ContextId, Status = current.Status,
                    },
                };
            }
            if (IsTerminal(current.Status?.State))
            {
                yield break;
            }
        }
    }

    /// <summary>A status is "the same status" while neither its state nor the message it carries has changed.</summary>
    private static string Fingerprint(global::A2A.TaskStatus? status) =>
        $"{status?.State}|{status?.Message?.MessageId}";

    private static bool IsTerminal(TaskState? state) => state is
        TaskState.Completed or TaskState.Canceled or TaskState.Failed or TaskState.Rejected
        or TaskState.InputRequired or TaskState.AuthRequired;

    /// <summary>Only an authenticated partner sees the private skill; the public card never mentions it.</summary>
    public Task<AgentCard> GetExtendedAgentCardAsync(GetExtendedAgentCardRequest request, CancellationToken cancellationToken = default)
    {
        _ = partners.Current; // throws when the caller is not an authenticated partner
        return Task.FromResult(AgentCardFactory.Signed(AgentCardFactory.Extended(a2a.Value), auth.Value));
    }

    public async Task<TaskPushNotificationConfig> CreateTaskPushNotificationConfigAsync(
        CreateTaskPushNotificationConfigRequest request, CancellationToken cancellationToken = default)
    {
        var push = request.Config ?? throw new A2AException("A push notification configuration is required.", A2AErrorCode.InvalidParams);
        var taskId = request.TaskId ?? throw new A2AException("A task id is required.", A2AErrorCode.InvalidParams);
        await EnsureTaskExistsAsync(taskId, cancellationToken);

        var id = (request.ConfigId ?? push.Id) is { Length: > 0 } given ? given : Guid.NewGuid().ToString("N");
        await using var ctx = await db.CreateDbContextAsync(cancellationToken);
        var existing = await ctx.A2APushConfigs.FirstOrDefaultAsync(c => c.Id == id && c.TaskId == taskId, cancellationToken);
        if (existing is null)
        {
            ctx.A2APushConfigs.Add(new A2APushConfigRow
            {
                Id = id, TaskId = taskId, Url = push.Url ?? "", Token = push.Token, CreatedAt = time.GetUtcNow().UtcDateTime,
            });
        }
        else
        {
            existing.Url = push.Url ?? "";
            existing.Token = push.Token;
        }
        await ctx.SaveChangesAsync(cancellationToken);
        push.Id = id;
        return new TaskPushNotificationConfig { Id = id, TaskId = taskId, PushNotificationConfig = push };
    }

    public async Task<TaskPushNotificationConfig> GetTaskPushNotificationConfigAsync(
        GetTaskPushNotificationConfigRequest request, CancellationToken cancellationToken = default)
    {
        await using var ctx = await db.CreateDbContextAsync(cancellationToken);
        var row = await ctx.A2APushConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.TaskId == request.TaskId && (request.Id == null || c.Id == request.Id), cancellationToken)
            ?? throw new A2AException("No such push notification configuration.", A2AErrorCode.TaskNotFound);
        return Wrap(row);
    }

    public async Task<ListTaskPushNotificationConfigResponse> ListTaskPushNotificationConfigAsync(
        ListTaskPushNotificationConfigRequest request, CancellationToken cancellationToken = default)
    {
        await using var ctx = await db.CreateDbContextAsync(cancellationToken);
        var rows = await ctx.A2APushConfigs.AsNoTracking()
            .Where(c => c.TaskId == request.TaskId)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(cancellationToken);
        return new ListTaskPushNotificationConfigResponse { Configs = [.. rows.Select(Wrap)] };
    }

    public async Task DeleteTaskPushNotificationConfigAsync(
        DeleteTaskPushNotificationConfigRequest request, CancellationToken cancellationToken = default)
    {
        await using var ctx = await db.CreateDbContextAsync(cancellationToken);
        await ctx.A2APushConfigs
            .Where(c => c.TaskId == request.TaskId && c.Id == request.Id)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private async Task EnsureTaskExistsAsync(string taskId, CancellationToken ct)
    {
        await using var ctx = await db.CreateDbContextAsync(ct);
        if (!await ctx.A2ATasks.AsNoTracking().AnyAsync(t => t.Id == taskId, ct))
        {
            throw new A2AException($"Task {taskId} does not exist.", A2AErrorCode.TaskNotFound);
        }
    }

    private static TaskPushNotificationConfig Wrap(A2APushConfigRow row) => new()
    {
        Id = row.Id,
        TaskId = row.TaskId,
        PushNotificationConfig = new PushNotificationConfig { Id = row.Id, Url = row.Url, Token = row.Token },
    };
}
