using System.Text.Json;
using System.Text.Json.Nodes;
using Maf.Lab.Domain.SharedState;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Maf.Lab.Hosting.Stores;

/// <summary>
/// A run's snapshot in the shared store, so a client that closed its tab can come back on any replica. Kept for
/// the run and a grace period after it: long enough to come back to, short enough not to be a second history.
/// </summary>
public sealed class RedisRunStateStore(IConnectionMultiplexer redis, IOptions<SharedStateOptions> options) : IRunStateStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static RedisKey Key(string runId) => $"run:{runId}";

    /// <summary>The key a process's heartbeat lives under while it runs (introduce-plugins decision 2).</summary>
    public static RedisKey HeartbeatKey(string processId) => $"heartbeat:{processId}";

    /// <summary>
    /// A compare-and-set: a stored terminal outcome is never replaced by a different one, whoever writes. The owner's own
    /// late write cannot undo a reader's `cancelled`, and a reader cannot undo a finished run. Every write that goes
    /// through sets the run-grace expiry again, as a plain write always did.
    /// </summary>
    private const string SaveScript = """
        local current = redis.call('GET', KEYS[1])
        if current then
          local ok, stored = pcall(cjson.decode, current)
          if ok and stored then
            local o = stored['outcome']
            if (o == 'answered' or o == 'failed' or o == 'cancelled') and o ~= ARGV[2] then
              return 0
            end
          end
        end
        redis.call('SET', KEYS[1], ARGV[1], 'PX', ARGV[3])
        return 1
        """;

    public async Task SaveAsync(RunState state, CancellationToken ct)
    {
        var value = JsonSerializer.Serialize(state, Json);
        // The grace period starts again on every write, so a long run is not forgotten while it is still running.
        await redis.GetDatabase().ScriptEvaluateAsync(SaveScript, [Key(state.RunId)],
            [value, state.Outcome, (long)options.Value.RunGrace.TotalMilliseconds]);
    }

    public async Task<RunState?> GetAsync(string runId, CancellationToken ct)
    {
        var db = redis.GetDatabase();
        var value = await db.StringGetAsync(Key(runId));
        if (value.IsNullOrEmpty || Read((string)value!) is not { } state)
        {
            return null;
        }
        // A run still running whose owner has no heartbeat was orphaned: its process died or was killed. It is closed
        // here, on read, through the same compare-and-set, so no page waits forever on it.
        if (state.Outcome == RunOutcomes.Running && state.Instance is { } owner && !await db.KeyExistsAsync(HeartbeatKey(owner)))
        {
            var cancelled = state with { Outcome = RunOutcomes.Cancelled, UpdatedAt = DateTimeOffset.UtcNow };
            await SaveAsync(cancelled, ct);
            var after = await db.StringGetAsync(Key(runId));
            return after.IsNullOrEmpty ? cancelled : Read((string)after!);
        }
        return state;
    }

    /// <summary>
    /// Reads a stored run state. A state written before rename-firm-to-tenant names its tenant <c>firmId</c>; it is read
    /// as <c>tenantId</c> until such states have aged out (they live for the run grace only).
    /// </summary>
    public static RunState? Read(string json)
    {
        if (JsonNode.Parse(json) is JsonObject node && node["tenantId"] is null && node["firmId"] is { } legacy)
        {
            node.Remove("firmId");
            node["tenantId"] = legacy.DeepClone();
            return node.Deserialize<RunState>(Json);
        }
        return JsonSerializer.Deserialize<RunState>(json, Json);
    }
}
