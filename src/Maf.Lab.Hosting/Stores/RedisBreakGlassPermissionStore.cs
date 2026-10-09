using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Maf.Lab.Domain.SharedState;
using StackExchange.Redis;

namespace Maf.Lab.Hosting.Stores;

/// <summary>
/// Atomic session permission and per-grant revocation fence. End acknowledgements require a verified standalone
/// Redis with synchronous AOF: Redis fsyncs each write batch before returning its replies.
/// </summary>
public sealed class RedisBreakGlassPermissionStore(IConnectionMultiplexer redis, TimeProvider clock)
    : IBreakGlassPermissionStore
{
    private static string Digest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static RedisKey ActiveKey(string sessionKey) => $"breakglass:{{{Digest(sessionKey)}}}:active";
    private static RedisKey EndedKey(string sessionKey, string grantId) => $"breakglass:{{{Digest(sessionKey)}}}:ended:{Digest(grantId)}";

    // TIME and PEXPIREAT make publication delay consume the original grant life, never restart it.
    // Tick strings preserve exact expiry equality without Lua's floating-point loss of DateTime ticks.
    private const string IdentityScript = """
        local info = redis.call('INFO', 'server')
        local runId = string.match(info, '\r\nrun_id:([^\r\n]+)')
        if not runId or runId ~= ARGV[#ARGV] then return redis.error_reply('Permission persistence identity changed') end
        """;

    private const string ActivateScript = IdentityScript + "\n" + """
        local t = redis.call('TIME')
        local now = tonumber(t[1]) * 1000 + math.floor(tonumber(t[2]) / 1000)
        local expiry = tonumber(ARGV[3])
        if not expiry or expiry <= now or redis.call('EXISTS', KEYS[2]) ~= 0 then return 0 end
        local kind = redis.call('TYPE', KEYS[1]).ok
        if kind ~= 'none' then
          if kind ~= 'hash' then return 0 end
          local p = redis.call('HMGET', KEYS[1], 'session', 'grant', 'expiry', 'ticks')
          if p[1] ~= ARGV[1] or p[2] ~= ARGV[2] or p[3] ~= ARGV[3] or p[4] ~= ARGV[4] then return 0 end
          local ttl = redis.call('PTTL', KEYS[1])
          if ttl <= 0 then return 0 end
          return 1
        end
        redis.call('HSET', KEYS[1], 'session', ARGV[1], 'grant', ARGV[2], 'expiry', ARGV[3], 'ticks', ARGV[4])
        redis.call('PEXPIREAT', KEYS[1], expiry)
        return 1
        """;

    private const string RevokeScript = IdentityScript + "\n" + """
        local t = redis.call('TIME')
        local now = tonumber(t[1]) * 1000 + math.floor(tonumber(t[2]) / 1000)
        local untilAt = math.max(tonumber(ARGV[2]) + 60000, now + 60000)
        local ttl = redis.call('PTTL', KEYS[2])
        if ttl > 0 then untilAt = math.max(untilAt, now + ttl) end
        redis.call('SET', KEYS[2], 'ended')
        redis.call('PEXPIREAT', KEYS[2], untilAt)
        if redis.call('TYPE', KEYS[1]).ok == 'hash' and redis.call('HGET', KEYS[1], 'grant') == ARGV[1] then
          redis.call('DEL', KEYS[1])
        end
        return runId
        """;

    private const string ReadScript = IdentityScript + "\n" + """
        if redis.call('TYPE', KEYS[1]).ok ~= 'hash' then return {} end
        local p = redis.call('HMGET', KEYS[1], 'session', 'grant', 'expiry', 'ticks')
        if not p[1] or not p[2] or not p[3] or not p[4] then return {} end
        local t = redis.call('TIME')
        local now = tonumber(t[1]) * 1000 + math.floor(tonumber(t[2]) / 1000)
        local expiry = tonumber(p[3])
        if not expiry or expiry <= now or redis.call('PTTL', KEYS[1]) <= 0 then return {} end
        return p
        """;

    public async Task<bool> ActivateAsync(ContentPermission permission, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(permission);
        Validate(permission.SessionKey, permission.GrantId);
        var proof = await InspectPersistenceAsync(ct);
        var result = await redis.GetDatabase().ScriptEvaluateAsync(ActivateScript,
            [ActiveKey(permission.SessionKey), EndedKey(permission.SessionKey, permission.GrantId)],
            [permission.SessionKey, permission.GrantId, permission.ExpiresAt.ToUnixTimeMilliseconds(),
                permission.ExpiresAt.UtcTicks.ToString(CultureInfo.InvariantCulture), proof.RunId]).WaitAsync(TimeSpan.FromSeconds(2), ct);
        if ((long)result != 1) return false;
        await VerifyProofAsync(proof, ct);
        return permission.ExpiresAt > clock.GetUtcNow();
    }

    public async Task RevokeAsync(string sessionKey, string grantId, DateTimeOffset expiresAt, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Validate(sessionKey, grantId);
        // A keyless WAITAOF through a multiplexer may run on another shard or a reconnected socket with offset
        // zero. Do not treat that as evidence that this revocation reached disk. The configured topology here
        // is one standalone server, and its checked appendfsync=always reply is itself the durability boundary.
        var proof = await InspectPersistenceAsync(ct);
        var result = await redis.GetDatabase().ScriptEvaluateAsync(RevokeScript,
            [ActiveKey(sessionKey), EndedKey(sessionKey, grantId)],
            [grantId, expiresAt.ToUnixTimeMilliseconds(), proof.RunId]).WaitAsync(TimeSpan.FromSeconds(2), ct);
        if ((string?)result != proof.RunId) throw PersistenceUnavailable();
        await VerifyProofAsync(proof, ct);
    }

    public async Task<ContentPermission?> ReadAsync(string sessionKey, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionKey);
        var proof = await InspectPersistenceAsync(ct);
        var result = await redis.GetDatabase().ScriptEvaluateAsync(ReadScript, [ActiveKey(sessionKey)], [proof.RunId])
            .WaitAsync(TimeSpan.FromSeconds(2), ct);
        var fields = (RedisResult[]?)result;
        if (fields is not { Length: 4 }) return null;
        var session = (string?)fields[0];
        var grant = (string?)fields[1];
        if (session != sessionKey || string.IsNullOrWhiteSpace(grant)
            || !long.TryParse((string?)fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out var milliseconds)
            || !long.TryParse((string?)fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks) return null;
        var expiry = new DateTimeOffset(ticks, TimeSpan.Zero);
        if (expiry.ToUnixTimeMilliseconds() != milliseconds || expiry <= clock.GetUtcNow()) return null;
        await VerifyProofAsync(proof, ct);
        if (expiry <= clock.GetUtcNow()) return null;
        return new ContentPermission(session, grant, expiry);
    }

    private static void Validate(string sessionKey, string grantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(grantId);
    }

    private static InvalidOperationException PersistenceUnavailable() =>
        new("Break-glass permission requires a reachable standalone Redis with synchronous healthy AOF persistence.");

    private async Task<PersistenceProof> InspectPersistenceAsync(CancellationToken ct)
    {
        var endpoints = redis.GetEndPoints();
        if (endpoints.Length != 1) throw PersistenceUnavailable();
        var server = redis.GetServer(endpoints[0]);
        return new PersistenceProof(server, await VerifyPersistenceAsync(server, ct), endpoints);
    }

    private async Task VerifyProofAsync(PersistenceProof proof, CancellationToken ct)
    {
        if (await VerifyPersistenceAsync(proof.Server, ct) != proof.RunId
            || !redis.GetEndPoints().SequenceEqual(proof.Endpoints)) throw PersistenceUnavailable();
    }

    private sealed record PersistenceProof(IServer Server, string RunId, System.Net.EndPoint[] Endpoints);

    private static async Task<string> VerifyPersistenceAsync(IServer server, CancellationToken ct)
    {
        if (!server.IsConnected || server.ServerType != ServerType.Standalone || server.IsReplica)
            throw PersistenceUnavailable();
        var before = await ReadInfoAsync(server, ct);
        // Only these non-secret settings are inspected. Runtime CONFIG GET permission is required; CONFIG SET
        // is never issued by this store. Persistence settings must remain fixed while serving permissions.
        var only = await server.ConfigGetAsync("appendonly").WaitAsync(TimeSpan.FromSeconds(2), ct);
        var sync = await server.ConfigGetAsync("appendfsync").WaitAsync(TimeSpan.FromSeconds(2), ct);
        var rewrite = await server.ConfigGetAsync("no-appendfsync-on-rewrite").WaitAsync(TimeSpan.FromSeconds(2), ct);
        var after = await ReadInfoAsync(server, ct);
        if (before["run_id"] != after["run_id"]
            || !Setting(only, "appendonly", "yes") || !Setting(sync, "appendfsync", "always")
            || !Setting(rewrite, "no-appendfsync-on-rewrite", "no")) throw PersistenceUnavailable();
        return after["run_id"];
    }

    private static bool Setting(KeyValuePair<string, string>[] values, string name, string expected) =>
        values is [{ Key: var key, Value: var value }] && key == name && value == expected;

    private static async Task<Dictionary<string, string>> ReadInfoAsync(IServer server, CancellationToken ct)
    {
        var result = await server.ExecuteAsync("INFO", ["server", "persistence", "replication"], CommandFlags.None)
            .WaitAsync(TimeSpan.FromSeconds(2), ct);
        var info = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in ((string?)result ?? "").Split('\n'))
        {
            var split = line.IndexOf(':');
            if (split > 0 && !line.StartsWith('#') && !info.TryAdd(line[..split], line[(split + 1)..].TrimEnd('\r')))
                throw PersistenceUnavailable();
        }
        if (!info.TryGetValue("run_id", out var run) || run.Length != 40 || !run.All(Uri.IsHexDigit)
            || !Match("redis_mode", "standalone") || !Match("role", "master") || !Match("connected_slaves", "0")
            || !Match("aof_enabled", "1") || !Match("aof_last_write_status", "ok")
            || !Match("aof_last_bgrewrite_status", "ok") || !Match("aof_rewrite_in_progress", "0")
            || !Match("aof_rewrite_scheduled", "0")) throw PersistenceUnavailable();
        return info;

        bool Match(string name, string value) => info.TryGetValue(name, out var actual) && actual == value;
    }
}
