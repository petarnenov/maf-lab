using System.Collections.Concurrent;
using System.Text.Json;
using Maf.Lab.A2A;
using Maf.Lab.TestGen;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Maf.Lab.TestAgent;

/// <summary>
/// What a task needs to go on after the process running it has stopped: the request, and — from the baseline on —
/// where the loop stood after the last finished attempt. It holds test code the agent wrote (the diffs), never the key.
/// </summary>
public sealed record TaskCheckpoint(TestGenRequest Request, RunCheckpoint? Run = null);

/// <summary>The loop's state after the baseline (<see cref="NextAttempt"/> 1) or after attempt <c>NextAttempt - 1</c>.</summary>
public sealed record RunCheckpoint(
    int NextAttempt,
    double? BaselinePct,
    double? Current,
    string? Feedback,
    IReadOnlyList<AttemptLog> Attempts,
    double? BestPct,
    string BestDiff,
    IReadOnlyList<SuspectedBug> BestBugs,
    IReadOnlyList<SuspectedBug> Bugs,
    string WorkingDiff,
    long LargestInput,
    long LargestOutput,
    TestGenUsage Usage,
    long LastSeq);

/// <summary>
/// Checkpoints and leases of the agent's tasks, beside the tasks in the shared store. A lease says a replica is running
/// the task right now; one that is not renewed lapses, and another replica may take the task over.
/// </summary>
public interface ITaskCheckpointStore
{
    Task<TaskCheckpoint?> GetAsync(string taskId, CancellationToken ct);
    Task SaveAsync(string taskId, TaskCheckpoint checkpoint, CancellationToken ct);
    Task DeleteAsync(string taskId, CancellationToken ct);

    /// <summary>Takes the lease if no one holds it.</summary>
    Task<bool> TakeLeaseAsync(string taskId, string owner, TimeSpan ttl, CancellationToken ct);

    /// <summary>Extends the lease if <paramref name="owner"/> still holds it; false when it does not.</summary>
    Task<bool> RenewLeaseAsync(string taskId, string owner, TimeSpan ttl, CancellationToken ct);

    /// <summary>Gives the lease up if <paramref name="owner"/> holds it.</summary>
    Task ReleaseLeaseAsync(string taskId, string owner, CancellationToken ct);

    Task<bool> IsLeasedAsync(string taskId, CancellationToken ct);
}

public sealed class RedisTaskCheckpointStore(IConnectionMultiplexer redis, IOptions<A2AOptions> options) : ITaskCheckpointStore
{
    /// <summary>As long as the task itself is kept (<see cref="RedisTaskStore"/>).</summary>
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    private const string RenewScript =
        "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('pexpire', KEYS[1], ARGV[2]) else return 0 end";
    private const string ReleaseScript =
        "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end";

    private RedisKey Checkpoint(string taskId) => $"task:{options.Value.StoreKeyspace}:checkpoint:{taskId}";
    private RedisKey Lease(string taskId) => $"task:{options.Value.StoreKeyspace}:lease:{taskId}";

    public async Task<TaskCheckpoint?> GetAsync(string taskId, CancellationToken ct)
    {
        var value = await redis.GetDatabase().StringGetAsync(Checkpoint(taskId));
        return value.IsNullOrEmpty ? null : JsonSerializer.Deserialize<TaskCheckpoint>((string)value!, TestGenKinds.Json);
    }

    public Task SaveAsync(string taskId, TaskCheckpoint checkpoint, CancellationToken ct) =>
        redis.GetDatabase().StringSetAsync(Checkpoint(taskId), JsonSerializer.Serialize(checkpoint, TestGenKinds.Json), Retention);

    public Task DeleteAsync(string taskId, CancellationToken ct) => redis.GetDatabase().KeyDeleteAsync(Checkpoint(taskId));

    public Task<bool> TakeLeaseAsync(string taskId, string owner, TimeSpan ttl, CancellationToken ct) =>
        redis.GetDatabase().StringSetAsync(Lease(taskId), owner, ttl, When.NotExists);

    public async Task<bool> RenewLeaseAsync(string taskId, string owner, TimeSpan ttl, CancellationToken ct) =>
        (long)await redis.GetDatabase().ScriptEvaluateAsync(RenewScript, [Lease(taskId)], [owner, (long)ttl.TotalMilliseconds]) == 1;

    public Task ReleaseLeaseAsync(string taskId, string owner, CancellationToken ct) =>
        redis.GetDatabase().ScriptEvaluateAsync(ReleaseScript, [Lease(taskId)], [owner]);

    public Task<bool> IsLeasedAsync(string taskId, CancellationToken ct) => redis.GetDatabase().KeyExistsAsync(Lease(taskId));
}

/// <summary>For a host without a shared store (tests, local runs): the same rules, in this process.</summary>
public sealed class InMemoryTaskCheckpointStore(TimeProvider time) : ITaskCheckpointStore
{
    private readonly ConcurrentDictionary<string, string> _checkpoints = new();
    private readonly Dictionary<string, (string Owner, DateTimeOffset Until)> _leases = [];

    public Task<TaskCheckpoint?> GetAsync(string taskId, CancellationToken ct) =>
        Task.FromResult(_checkpoints.TryGetValue(taskId, out var json) ? JsonSerializer.Deserialize<TaskCheckpoint>(json, TestGenKinds.Json) : null);

    public Task SaveAsync(string taskId, TaskCheckpoint checkpoint, CancellationToken ct)
    {
        _checkpoints[taskId] = JsonSerializer.Serialize(checkpoint, TestGenKinds.Json);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string taskId, CancellationToken ct)
    {
        _checkpoints.TryRemove(taskId, out _);
        return Task.CompletedTask;
    }

    public Task<bool> TakeLeaseAsync(string taskId, string owner, TimeSpan ttl, CancellationToken ct)
    {
        lock (_leases)
        {
            if (Held(taskId) is not null)
            {
                return Task.FromResult(false);
            }
            _leases[taskId] = (owner, time.GetUtcNow() + ttl);
            return Task.FromResult(true);
        }
    }

    public Task<bool> RenewLeaseAsync(string taskId, string owner, TimeSpan ttl, CancellationToken ct)
    {
        lock (_leases)
        {
            if (Held(taskId) != owner)
            {
                return Task.FromResult(false);
            }
            _leases[taskId] = (owner, time.GetUtcNow() + ttl);
            return Task.FromResult(true);
        }
    }

    public Task ReleaseLeaseAsync(string taskId, string owner, CancellationToken ct)
    {
        lock (_leases)
        {
            if (Held(taskId) == owner)
            {
                _leases.Remove(taskId);
            }
        }
        return Task.CompletedTask;
    }

    public Task<bool> IsLeasedAsync(string taskId, CancellationToken ct)
    {
        lock (_leases)
        {
            return Task.FromResult(Held(taskId) is not null);
        }
    }

    private string? Held(string taskId) =>
        _leases.TryGetValue(taskId, out var lease) && lease.Until > time.GetUtcNow() ? lease.Owner : null;
}
