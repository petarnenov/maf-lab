using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Maf.Lab.Domain.SharedState;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Hosting.Stores;
using Maf.Lab.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using StackExchange.Redis;
using Role = Maf.Lab.Domain.Tenancy.Role;

namespace Maf.Lab.Tests;

public sealed class BreakGlassPermissionStoreTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Retrying_a_publication_preserves_its_original_expiry_and_denies_replacement()
    {
        var time = new FakeTimeProvider();
        var store = new FakeBreakGlassPermissionStore(time);
        var grant = new ContentPermission("session", "grant", time.GetUtcNow().AddMinutes(5));
        Assert.True(await store.ActivateAsync(grant, Ct));
        time.Advance(TimeSpan.FromMinutes(2));
        Assert.True(await store.ActivateAsync(grant, Ct));
        Assert.False(await store.ActivateAsync(grant with { ExpiresAt = grant.ExpiresAt.AddTicks(1) }, Ct));
        Assert.False(await store.ActivateAsync(grant with { GrantId = "different" }, Ct));
        Assert.Equal(grant, await store.ReadAsync(grant.SessionKey, Ct));
        time.Advance(TimeSpan.FromMinutes(3));
        Assert.Null(await store.ReadAsync(grant.SessionKey, Ct));
        Assert.False(await store.ActivateAsync(grant, Ct));
    }

    [Fact]
    public async Task Ending_before_publication_fences_delayed_activation_but_allows_a_new_grant()
    {
        var time = new FakeTimeProvider();
        var store = new FakeBreakGlassPermissionStore(time);
        var grant = new ContentPermission("session", "ended", time.GetUtcNow().AddMinutes(5));
        await store.RevokeAsync(grant.SessionKey, grant.GrantId, grant.ExpiresAt, Ct);
        Assert.False(await store.ActivateAsync(grant, Ct));
        var next = grant with { GrantId = "new" };
        Assert.True(await store.ActivateAsync(next, Ct));
        await store.RevokeAsync(grant.SessionKey, grant.GrantId, grant.ExpiresAt, Ct);
        Assert.Equal(next, await store.ReadAsync(grant.SessionKey, Ct));
        await store.RevokeAsync(next.SessionKey, next.GrantId, next.ExpiresAt, Ct);
        Assert.Null(await store.ReadAsync(next.SessionKey, Ct));
        Assert.False(await store.ActivateAsync(next, Ct));
    }

    [Fact]
    public async Task Sessions_are_isolated_and_revoke_never_shortens_an_existing_fence()
    {
        var time = new FakeTimeProvider();
        var store = new FakeBreakGlassPermissionStore(time);
        var grant = new ContentPermission("session-a", "grant", time.GetUtcNow().AddMinutes(5));
        var other = grant with { SessionKey = "session-b" };
        Assert.True(await store.ActivateAsync(other, Ct));
        await store.RevokeAsync(grant.SessionKey, grant.GrantId, grant.ExpiresAt, Ct);
        await store.RevokeAsync(grant.SessionKey, grant.GrantId, time.GetUtcNow(), Ct);
        time.Advance(TimeSpan.FromMinutes(2));
        Assert.False(await store.ActivateAsync(grant, Ct));
        Assert.Equal(other, await store.ReadAsync(other.SessionKey, Ct));
    }

    [Fact]
    public void Session_keys_match_the_existing_audit_tuple_and_include_every_identity_boundary()
    {
        var principal = new Principal("operator", TenantId.Firm("firm-a"), Role.PLATFORM_ADMIN);
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new[] { "issuer", "session", "operator", "firm-a" }))));
        Assert.Equal(expected, OperatorSessionKey.Create("issuer", "session", principal));
        Assert.NotEqual(expected, OperatorSessionKey.Create("other", "session", principal));
        Assert.NotEqual(expected, OperatorSessionKey.Create("issuer", "other", principal));
        Assert.NotEqual(expected, OperatorSessionKey.Create("issuer", "session", principal with { UserId = "other" }));
        Assert.NotEqual(expected, OperatorSessionKey.Create("issuer", "session", principal with { TenantId = TenantId.Firm("firm-b") }));
        Assert.Equal(expected, OperatorSessionKey.Create("issuer", "session", principal with { Role = Role.USER }));
        Assert.NotEqual(OperatorSessionKey.Create("a", "b\"c", principal), OperatorSessionKey.Create("a\"b", "c", principal));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("foreign-session")]
    [InlineData("empty-grant")]
    [InlineData("bad-ticks")]
    [InlineData("out-of-range")]
    [InlineData("mismatched-expiry")]
    [InlineData("expired")]
    public async Task Redis_read_refuses_incomplete_corrupt_or_expired_permissions(string fault)
    {
        var time = new FakeTimeProvider();
        var expiry = time.GetUtcNow().AddMinutes(5);
        RedisValue[] fields = ["session", "grant", expiry.ToUnixTimeMilliseconds(), expiry.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture)];
        switch (fault)
        {
            case "missing": fields = []; break;
            case "foreign-session": fields[0] = "other"; break;
            case "empty-grant": fields[1] = " "; break;
            case "bad-ticks": fields[3] = "bad"; break;
            case "out-of-range": fields[3] = long.MaxValue; break;
            case "mismatched-expiry": fields[2] = expiry.ToUnixTimeMilliseconds() + 1; break;
            case "expired": time.Advance(TimeSpan.FromMinutes(5)); break;
        }
        var database = Substitute.For<IDatabase>();
        database.ScriptEvaluateAsync(Arg.Any<string>(), Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>(), Arg.Any<CommandFlags>())
            .Returns(RedisResult.Create(fields));
        var redis = Substitute.For<IConnectionMultiplexer>();
        redis.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(database);
        ConfigureDurableServer(redis);
        var store = new RedisBreakGlassPermissionStore(redis, time);
        Assert.Null(await store.ReadAsync("session", Ct));
    }

    [Theory]
    [InlineData("activate")]
    [InlineData("revoke")]
    [InlineData("read")]
    public async Task A_cancelled_wait_returns_while_the_queued_Redis_operation_is_still_pending(string operation)
    {
        var pending = new TaskCompletionSource<RedisResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var database = Substitute.For<IDatabase>();
        database.ScriptEvaluateAsync(Arg.Any<string>(), Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>(), Arg.Any<CommandFlags>())
            .Returns(pending.Task);
        var redis = Substitute.For<IConnectionMultiplexer>();
        redis.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(database);
        ConfigureDurableServer(redis);
        var store = new RedisBreakGlassPermissionStore(redis, TimeProvider.System);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var expiry = DateTimeOffset.UtcNow.AddMinutes(5);
        Task result = operation switch
        {
            "activate" => store.ActivateAsync(new ContentPermission("session", "grant", expiry), cancellation.Token),
            "revoke" => store.RevokeAsync("session", "grant", expiry, cancellation.Token),
            _ => store.ReadAsync("session", cancellation.Token),
        };
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => result);
        Assert.False(pending.Task.IsCompleted);
        pending.SetResult(RedisResult.Create((RedisValue)0));
    }

    [Theory]
    [InlineData("appendonly")]
    [InlineData("appendfsync")]
    [InlineData("no-appendfsync-on-rewrite")]
    [InlineData("unhealthy-aof")]
    [InlineData("aof-rewrite")]
    [InlineData("replica")]
    [InlineData("cluster")]
    [InlineData("multiple-servers")]
    [InlineData("changed-process")]
    public async Task Persistence_policy_or_identity_failure_never_acknowledges_or_publishes_revocation(string fault)
    {
        var database = Substitute.For<IDatabase>();
        var redis = Substitute.For<IConnectionMultiplexer>();
        redis.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(database);
        var server = ConfigureDurableServer(redis);
        if (fault is "appendonly" or "appendfsync" or "no-appendfsync-on-rewrite")
            server.ConfigGetAsync(fault).Returns([new KeyValuePair<string, string>(fault, "unsafe")]);
        if (fault == "unhealthy-aof") SetInfo(server, HealthyInfo.Replace("aof_last_write_status:ok", "aof_last_write_status:err", StringComparison.Ordinal));
        if (fault == "aof-rewrite") SetInfo(server, HealthyInfo.Replace("aof_rewrite_in_progress:0", "aof_rewrite_in_progress:1", StringComparison.Ordinal));
        if (fault == "replica") server.IsReplica.Returns(true);
        if (fault == "cluster") server.ServerType.Returns(ServerType.Cluster);
        if (fault == "multiple-servers") redis.GetEndPoints(Arg.Any<bool>()).Returns([new System.Net.DnsEndPoint("fixture", 6379), new System.Net.DnsEndPoint("fixture-other", 6379)]);
        if (fault == "changed-process") server.ExecuteAsync("INFO", Arg.Any<ICollection<object>>(), Arg.Any<CommandFlags>())
            .Returns(RedisResult.Create((RedisValue)HealthyInfo), RedisResult.Create((RedisValue)HealthyInfo.Replace(RunId, OtherRunId, StringComparison.Ordinal)));
        var store = new RedisBreakGlassPermissionStore(redis, TimeProvider.System);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RevokeAsync("session", "grant", DateTimeOffset.UtcNow.AddMinutes(5), Ct));
        // All permission entry points reject unsafe deployments, including direct content readers.
        if (fault != "changed-process")
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.ActivateAsync(new ContentPermission("session", "grant", DateTimeOffset.UtcNow.AddMinutes(5)), Ct));
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReadAsync("session", Ct));
        }
        await database.DidNotReceive().ScriptEvaluateAsync(Arg.Any<string>(), Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>(), Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task An_unknown_write_outcome_does_not_acknowledge_and_a_retry_checks_and_rewrites_the_fence()
    {
        var database = Substitute.For<IDatabase>();
        var redis = Substitute.For<IConnectionMultiplexer>();
        redis.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(database);
        ConfigureDurableServer(redis);
        database.ScriptEvaluateAsync(Arg.Any<string>(), Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>(), Arg.Any<CommandFlags>())
            .Returns(Task.FromException<RedisResult>(new RedisConnectionException(ConnectionFailureType.SocketFailure, CommandFlags.None, "fixture failure")),
                Task.FromResult(RedisResult.Create((RedisValue)RunId)));
        var store = new RedisBreakGlassPermissionStore(redis, TimeProvider.System);
        var expiry = DateTimeOffset.UtcNow.AddMinutes(5);
        await Assert.ThrowsAsync<RedisConnectionException>(() => store.RevokeAsync("session", "grant", expiry, Ct));
        await store.RevokeAsync("session", "grant", expiry, Ct);
        await database.Received(2).ScriptEvaluateAsync(Arg.Any<string>(), Arg.Any<RedisKey[]>(),
            Arg.Is<RedisValue[]>(args => args[0] == "grant" && (long)args[1] == expiry.ToUnixTimeMilliseconds() && args[2] == RunId), Arg.Any<CommandFlags>());
    }

    [Theory]
    [InlineData("write-server")]
    [InlineData("post-write-server")]
    [InlineData("post-write-policy")]
    public async Task A_write_reply_is_not_an_acknowledgement_when_its_server_or_durability_proof_changed(string fault)
    {
        var database = Substitute.For<IDatabase>();
        var redis = Substitute.For<IConnectionMultiplexer>();
        redis.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(database);
        var server = ConfigureDurableServer(redis);
        database.ScriptEvaluateAsync(Arg.Any<string>(), Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>(), Arg.Any<CommandFlags>())
            .Returns(RedisResult.Create((RedisValue)(fault == "write-server" ? OtherRunId : RunId)));
        if (fault == "post-write-server") server.ExecuteAsync("INFO", Arg.Any<ICollection<object>>(), Arg.Any<CommandFlags>())
            .Returns(RedisResult.Create((RedisValue)HealthyInfo), RedisResult.Create((RedisValue)HealthyInfo),
                RedisResult.Create((RedisValue)HealthyInfo.Replace(RunId, OtherRunId, StringComparison.Ordinal)));
        if (fault == "post-write-policy") server.ConfigGetAsync("appendfsync")
            .Returns([new KeyValuePair<string, string>("appendfsync", "always")], [new KeyValuePair<string, string>("appendfsync", "everysec")]);
        var store = new RedisBreakGlassPermissionStore(redis, TimeProvider.System);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RevokeAsync("session", "grant", DateTimeOffset.UtcNow.AddMinutes(5), Ct));
    }

    private const string RunId = "1111111111111111111111111111111111111111";
    private const string OtherRunId = "2222222222222222222222222222222222222222";
    private const string HealthyInfo = "# Server\r\nrun_id:" + RunId + "\r\nredis_mode:standalone\r\nrole:master\r\nconnected_slaves:0\r\naof_enabled:1\r\naof_last_write_status:ok\r\naof_last_bgrewrite_status:ok\r\naof_rewrite_in_progress:0\r\naof_rewrite_scheduled:0\r\n";

    private static IServer ConfigureDurableServer(IConnectionMultiplexer redis)
    {
        var endpoint = new System.Net.DnsEndPoint("fixture", 6379);
        redis.GetEndPoints(Arg.Any<bool>()).Returns([endpoint]);
        var server = Substitute.For<IServer>();
        redis.GetServer(Arg.Any<System.Net.EndPoint>(), Arg.Any<object?>()).Returns(server);
        server.IsConnected.Returns(true);
        server.ServerType.Returns(ServerType.Standalone);
        server.ConfigGetAsync("appendonly").Returns([new KeyValuePair<string, string>("appendonly", "yes")]);
        server.ConfigGetAsync("appendfsync").Returns([new KeyValuePair<string, string>("appendfsync", "always")]);
        server.ConfigGetAsync("no-appendfsync-on-rewrite").Returns([new KeyValuePair<string, string>("no-appendfsync-on-rewrite", "no")]);
        SetInfo(server, HealthyInfo);
        return server;
    }

    private static void SetInfo(IServer server, string value) =>
        server.ExecuteAsync("INFO", Arg.Any<ICollection<object>>(), Arg.Any<CommandFlags>()).Returns(RedisResult.Create((RedisValue)value));
}
