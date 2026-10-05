using System.Text.Json;
using A2A;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Maf.Lab.A2A;

/// <summary>
/// An agent's tasks in the store its replicas share (the compliance reviewer's and the test agent's, each under its
/// own <see cref="A2AOptions.StoreKeyspace"/>). The SDK ships only <see cref="InMemoryTaskStore"/>, and
/// two replicas behind a balancer cannot share one: a task started on one is invisible on the other, and a caller
/// asking the wrong replica is told its task does not exist.
///
/// Unlike the assistant, this agent has no database and no page that reads its tasks alongside anything else, so
/// there is nothing here to join and the shared store is simply where they live.
/// </summary>
public sealed class RedisTaskStore(IConnectionMultiplexer redis, TimeProvider time, IOptions<A2AOptions> options) : ITaskStore
{
    private static readonly JsonSerializerOptions Json = A2AJsonUtilities.DefaultOptions;

    /// <summary>Long enough to outlive any caller still following a review, and no longer.</summary>
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    /// <summary>The states a task does not leave, as the SDK writes them.</summary>
    internal static readonly string[] Terminal =
        [.. new[] { TaskState.Completed, TaskState.Canceled, TaskState.Failed, TaskState.Rejected }.Select(StateName)];

    /// <summary>
    /// Saves a task unless the store already holds it in a terminal state the new copy would change: read and write are
    /// one step in Redis, so a replica whose work is still winding down cannot write over a cancel another replica
    /// recorded. ARGV: the task, its life in ms, its state, then the terminal states.
    /// </summary>
    internal const string SaveScript = """
        local current = redis.call('GET', KEYS[1])
        if current then
          local ok, stored = pcall(cjson.decode, current)
          if ok and type(stored) == 'table' and type(stored['status']) == 'table' then
            local state = stored['status']['state']
            if state ~= ARGV[3] then
              for i = 4, #ARGV do
                if ARGV[i] == state then return 0 end
              end
            end
          end
        end
        redis.call('SET', KEYS[1], ARGV[1], 'PX', ARGV[2])
        return 1
        """;

    /// <summary>The tasks, newest last, so a listing does not have to look at every key in the store.</summary>
    private RedisKey Index => $"task:{options.Value.StoreKeyspace}:index";

    private RedisKey Key(string taskId) => $"task:{options.Value.StoreKeyspace}:{taskId}";

    public async Task<AgentTask?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        var value = await redis.GetDatabase().StringGetAsync(Key(taskId));
        return value.IsNullOrEmpty ? null : JsonSerializer.Deserialize<AgentTask>((string)value!, Json);
    }

    public async Task SaveTaskAsync(string taskId, AgentTask task, CancellationToken cancellationToken = default)
    {
        var database = redis.GetDatabase();
        RedisValue[] args =
        [
            JsonSerializer.Serialize(task, Json),
            (long)Retention.TotalMilliseconds,
            task.Status?.State is { } state ? StateName(state) : "",
            .. Terminal.Select(t => (RedisValue)t),
        ];
        var saved = (long)await database.ScriptEvaluateAsync(SaveScript, [Key(taskId)], args);
        if (saved == 1)
        {
            await database.SortedSetAddAsync(Index, taskId, time.GetUtcNow().ToUnixTimeMilliseconds());
        }
    }

    /// <summary>A state's name exactly as the SDK serialises it into a stored task.</summary>
    private static string StateName(TaskState state) => JsonSerializer.Serialize(state, Json).Trim('"');

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
