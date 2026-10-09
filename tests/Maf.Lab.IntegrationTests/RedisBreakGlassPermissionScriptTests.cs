using System.Security.Cryptography;
using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Maf.Lab.Domain.SharedState;
using Maf.Lab.Hosting.Stores;
using StackExchange.Redis;

namespace Maf.Lab.IntegrationTests;

/// <summary>Runs the production permission/fence Lua against a native Redis server.</summary>
public sealed class RedisBreakGlassPermissionScriptTests : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly IContainer _redis = new ContainerBuilder("redis:8.8.3-alpine")
        .WithPortBinding(6379, true)
        .WithCommand("redis-server", "--appendonly", "yes", "--appendfsync", "always", "--no-appendfsync-on-rewrite", "no")
        .WithCreateParameterModifier(parameters => parameters.StopSignal = "SIGKILL")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("redis-cli", "ping"))
        .Build();
    private ConnectionMultiplexer _connection = null!;
    private RedisBreakGlassPermissionStore _store = null!;

    public async ValueTask InitializeAsync()
    {
        await _redis.StartAsync(Ct);
        _connection = await ConnectAsync();
        _store = new RedisBreakGlassPermissionStore(_connection, TimeProvider.System);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null) await _connection.DisposeAsync();
        await _redis.DisposeAsync();
    }

    private static RedisKey Active(string session) => $"breakglass:{{{Digest(session)}}}:active";
    private static RedisKey Ended(string session, string grant) => $"breakglass:{{{Digest(session)}}}:ended:{Digest(grant)}";
    private static string Digest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private Task<ConnectionMultiplexer> ConnectAsync() => ConnectionMultiplexer.ConnectAsync(
        $"{_redis.Hostname}:{_redis.GetMappedPublicPort(6379)},allowAdmin=true");

    [Fact]
    public async Task Publication_retry_cannot_replace_extend_or_retime_the_original_grant()
    {
        var permission = new ContentPermission("original", "grant", DateTimeOffset.UtcNow.AddMinutes(5));
        Assert.True(await _store.ActivateAsync(permission, Ct));
        Assert.True(await _store.ActivateAsync(permission, Ct));
        Assert.False(await _store.ActivateAsync(permission with { GrantId = "other" }, Ct));
        Assert.False(await _store.ActivateAsync(permission with { ExpiresAt = permission.ExpiresAt.AddMinutes(5) }, Ct));
        Assert.False(await _store.ActivateAsync(permission with { ExpiresAt = permission.ExpiresAt.AddTicks(1) }, Ct));
        Assert.Equal(permission, await _store.ReadAsync(permission.SessionKey, Ct));
        var ttl = await _connection.GetDatabase().KeyTimeToLiveAsync(Active(permission.SessionKey));
        Assert.NotNull(ttl);
        // Redis/container and host clocks can differ by a few milliseconds. Verify the authoritative absolute
        // deadline, then use TTL only as a bounded sanity check rather than pretending the clocks are identical.
        var expiry = (long)await _connection.GetDatabase().ExecuteAsync("PEXPIRETIME", Active(permission.SessionKey));
        Assert.Equal(permission.ExpiresAt.ToUnixTimeMilliseconds(), expiry);
        Assert.InRange(ttl!.Value, TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(5).Add(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task Revoke_fences_an_unpublished_grant_and_late_revocation_preserves_a_new_grant()
    {
        var permission = new ContentPermission("late", "ended", DateTimeOffset.UtcNow.AddMinutes(5));
        await _store.RevokeAsync(permission.SessionKey, permission.GrantId, permission.ExpiresAt, Ct);
        Assert.False(await _store.ActivateAsync(permission, Ct));
        var next = permission with { GrantId = "next" };
        Assert.True(await _store.ActivateAsync(next, Ct));
        await _store.RevokeAsync(permission.SessionKey, permission.GrantId, permission.ExpiresAt, Ct);
        Assert.Equal(next, await _store.ReadAsync(next.SessionKey, Ct));
        await _store.RevokeAsync(next.SessionKey, next.GrantId, next.ExpiresAt, Ct);
        Assert.Null(await _store.ReadAsync(next.SessionKey, Ct));
        Assert.False(await _store.ActivateAsync(next, Ct));
    }

    [Fact]
    public async Task Concurrent_publication_and_revocation_always_leave_the_grant_ended()
    {
        var permission = new ContentPermission("race", "grant", DateTimeOffset.UtcNow.AddMinutes(5));
        await Task.WhenAll(_store.ActivateAsync(permission, Ct),
            _store.RevokeAsync(permission.SessionKey, permission.GrantId, permission.ExpiresAt, Ct));
        Assert.Null(await _store.ReadAsync(permission.SessionKey, Ct));
        Assert.False(await _store.ActivateAsync(permission, Ct));
    }

    [Fact]
    public async Task Redis_server_time_rejects_past_expiry_even_when_the_replica_clock_lags()
    {
        var permission = new ContentPermission("expired", "grant", DateTimeOffset.UtcNow.AddMinutes(-1));
        var clock = new FixedTime(permission.ExpiresAt.AddMinutes(-1));
        var lagging = new RedisBreakGlassPermissionStore(_connection, clock);
        Assert.False(await lagging.ActivateAsync(permission, Ct));
        Assert.Null(await lagging.ReadAsync(permission.SessionKey, Ct));
        await lagging.RevokeAsync(permission.SessionKey, permission.GrantId, permission.ExpiresAt, Ct);
        var ttl = await _connection.GetDatabase().KeyTimeToLiveAsync(Ended(permission.SessionKey, permission.GrantId));
        Assert.NotNull(ttl);
        Assert.InRange(ttl!.Value, TimeSpan.FromSeconds(50), TimeSpan.FromSeconds(60));
    }

    [Theory]
    [InlineData("string")]
    [InlineData("missing-field")]
    [InlineData("bad-expiry")]
    [InlineData("no-ttl")]
    public async Task Corrupt_active_state_never_authorizes_or_accepts_publication(string fault)
    {
        var session = "corrupt-" + fault;
        var permission = new ContentPermission(session, "grant", DateTimeOffset.UtcNow.AddMinutes(5));
        var database = _connection.GetDatabase();
        if (fault == "string") await database.StringSetAsync(Active(session), "not-a-permission");
        else
        {
            await database.HashSetAsync(Active(session),
                [new HashEntry("session", session), new HashEntry("grant", "grant"),
                    new HashEntry("expiry", fault == "bad-expiry" ? "bad" : permission.ExpiresAt.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    new HashEntry("ticks", permission.ExpiresAt.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture))]);
            if (fault == "missing-field") await database.HashDeleteAsync(Active(session), "ticks");
            if (fault != "no-ttl") await database.KeyExpireAsync(Active(session), TimeSpan.FromMinutes(5));
        }
        Assert.Null(await _store.ReadAsync(session, Ct));
        Assert.False(await _store.ActivateAsync(permission, Ct));
    }

    [Fact]
    public async Task An_acknowledged_end_remains_ended_after_SIGKILL_and_AOF_recovery()
    {
        var permission = new ContentPermission("crash", "grant", DateTimeOffset.UtcNow.AddMinutes(5));
        Assert.True(await _store.ActivateAsync(permission, Ct));
        await _store.RevokeAsync(permission.SessionKey, permission.GrantId, permission.ExpiresAt, Ct);
        await _connection.DisposeAsync();
        // StopSignal=SIGKILL ensures this exercises AOF recovery, with no graceful SHUTDOWN/save.
        await _redis.StopAsync(Ct);
        await _redis.StartAsync(Ct);
        _connection = await ConnectAsync();
        _store = new RedisBreakGlassPermissionStore(_connection, TimeProvider.System);
        Assert.Null(await _store.ReadAsync(permission.SessionKey, Ct));
        Assert.True(await _connection.GetDatabase().KeyExistsAsync(Ended(permission.SessionKey, permission.GrantId)));
        Assert.False(await _store.ActivateAsync(permission, Ct));
    }

    [Theory]
    [InlineData("appendonly", "no")]
    [InlineData("appendfsync", "everysec")]
    [InlineData("no-appendfsync-on-rewrite", "yes")]
    public async Task Unsafe_persistence_cannot_acknowledge_an_end(string setting, string value)
    {
        var permission = new ContentPermission("unsafe", "grant", DateTimeOffset.UtcNow.AddMinutes(5));
        Assert.True(await _store.ActivateAsync(permission, Ct));
        var server = _connection.GetServer(_connection.GetEndPoints().Single());
        await server.ConfigSetAsync(setting, value);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _store.RevokeAsync(permission.SessionKey, permission.GrantId, permission.ExpiresAt, Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _store.ReadAsync(permission.SessionKey, Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _store.ActivateAsync(permission with { SessionKey = "unpublished" }, Ct));
        Assert.Equal(permission.GrantId, (string?)await _connection.GetDatabase().HashGetAsync(Active(permission.SessionKey), "grant"));
        Assert.False(await _connection.GetDatabase().KeyExistsAsync(Ended(permission.SessionKey, permission.GrantId)));
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
