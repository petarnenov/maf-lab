using System.Text.Json;
using A2A;
using StackExchange.Redis;

namespace Maf.Lab.ComplianceAgent;

/// <summary>
/// The reviewer's tasks in the store its replicas share. The SDK ships only <see cref="InMemoryTaskStore"/>, and
/// two replicas behind a balancer cannot share one: a task started on one is invisible on the other, and a caller
/// asking the wrong replica is told its task does not exist.
///
/// Unlike the assistant, this agent has no database and no page that reads its tasks alongside anything else, so
/// there is nothing here to join and the shared store is simply where they live.
/// </summary>
public sealed class RedisTaskStore(IConnectionMultiplexer redis, TimeProvider time) : ITaskStore
{
    private static readonly JsonSerializerOptions Json = A2AJsonUtilities.DefaultOptions;

    /// <summary>Long enough to outlive any caller still following a review, and no longer.</summary>
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    /// <summary>The tasks, newest last, so a listing does not have to look at every key in the store.</summary>
    private static readonly RedisKey Index = "task:compliance:index";

    private static RedisKey Key(string taskId) => $"task:compliance:{taskId}";

    public async Task<AgentTask?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        var value = await redis.GetDatabase().StringGetAsync(Key(taskId));
        return value.IsNullOrEmpty ? null : JsonSerializer.Deserialize<AgentTask>((string)value!, Json);
    }

    public async Task SaveTaskAsync(string taskId, AgentTask task, CancellationToken cancellationToken = default)
    {
        var database = redis.GetDatabase();
        await database.StringSetAsync(Key(taskId), JsonSerializer.Serialize(task, Json), Retention);
        await database.SortedSetAddAsync(Index, taskId, time.GetUtcNow().ToUnixTimeMilliseconds());
    }

    public async Task DeleteTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        var database = redis.GetDatabase();
        await database.KeyDeleteAsync(Key(taskId));
        await database.SortedSetRemoveAsync(Index, taskId);
    }

    public async Task<ListTasksResponse> ListTasksAsync(ListTasksRequest request, CancellationToken cancellationToken = default)
    {
        var size = request.PageSize is > 0 and <= 200 ? request.PageSize.Value : 50;
        var database = redis.GetDatabase();
        // Newest first, then read only as many as were asked for: a reviewer has tens of tasks, not millions.
        var ids = await database.SortedSetRangeByRankAsync(Index, 0, -1, Order.Descending);

        var tasks = new List<AgentTask>();
        foreach (var id in ids)
        {
            if (tasks.Count >= size)
            {
                break;
            }
            if (await GetTaskAsync(id!, cancellationToken) is not { } task)
            {
                // Expired out from under the index; the index catches up rather than the listing failing.
                await database.SortedSetRemoveAsync(Index, id);
                continue;
            }
            if (!string.IsNullOrWhiteSpace(request.ContextId) && task.ContextId != request.ContextId)
            {
                continue;
            }
            if (request.Status is { } status && task.Status?.State != status)
            {
                continue;
            }
            tasks.Add(task);
        }

        return new ListTasksResponse { Tasks = tasks, PageSize = size, TotalSize = tasks.Count };
    }
}
