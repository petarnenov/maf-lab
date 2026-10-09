using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maf.Lab.Api.Agent;
using Maf.Lab.Api.Compliance;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Configuration;
using Maf.Lab.Domain.SharedState;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Retrieval.Auth;
using Maf.Lab.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Maf.Lab.Tests;

public sealed class ContentAccessGrantTests
{
    private const string Sid = "sensitive-native-session";
    private static Principal Operator => TenantPluginFixture.Operator;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static string Key(string sid = Sid, Principal? actor = null) =>
        OperatorSessionKey.Create(new AuthOptions().Issuer, sid, actor ?? Operator);
    private static ContentAccessGrants Service(TenantPluginFixture fixture, Store store) => new(fixture.Db,
        new ToolAudit(fixture.Db, NullLogger<ToolAudit>.Instance, fixture.Clock), store, fixture.Clock);

    [Fact]
    public async Task Permission_publication_observes_an_already_committed_grant_and_chained_audit()
    {
        await using var fixture = new TenantPluginFixture();
        await fixture.InitializeAsync(Ct);
        var store = new Store(fixture.Clock)
        {
            BeforeActivate = async permission =>
            {
                await using var db = await fixture.Db.CreateDbContextAsync(Ct);
                Assert.Equal(permission.GrantId, Assert.Single(await db.ContentAccessGrants.ToListAsync(Ct)).Id.ToString(CultureInfo.InvariantCulture));
                var audit = Assert.Single(await db.Audit.ToListAsync(Ct));
                Assert.Equal(ContentAccessGrants.StartedKind, audit.Kind);
                Assert.True(AuditChain.Verify([audit]).Intact);
                Assert.DoesNotContain(Sid, audit.Arguments);
                Assert.DoesNotContain(Key(), audit.Arguments);
            },
        };

        var result = await Service(fixture, store).IssueAsync(Operator, Key(), "INC-123", 10, Ct);

        Assert.Equal(ContentGrantStatus.Created, result.Status);
        Assert.True(result.Envelope.Active);
        Assert.True(await Service(fixture, store).IsAllowedAsync(Operator, Key(), Ct));
        var json = JsonSerializer.Serialize(result.Envelope);
        Assert.DoesNotContain(Sid, json);
        Assert.DoesNotContain(Key(), json);
    }

    [Fact]
    public async Task Start_audit_failure_rolls_back_permission_and_grant()
    {
        await using var fixture = new TenantPluginFixture();
        await fixture.InitializeAsync(Ct);
        await using (var db = await fixture.Db.CreateDbContextAsync(Ct))
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER deny_grant_audit BEFORE INSERT ON "Audit"
                WHEN NEW."Kind" = 'operator.content-access.start' BEGIN SELECT RAISE(ABORT, 'fixture failure'); END;
                """, Ct);
        var store = new Store(fixture.Clock);

        await Assert.ThrowsAsync<DbUpdateException>(() => Service(fixture, store).IssueAsync(Operator, Key(), "INC-123", 10, Ct));

        Assert.Equal(0, store.Activations);
        await using var saved = await fixture.Db.CreateDbContextAsync(Ct);
        Assert.Empty(await saved.ContentAccessGrants.ToListAsync(Ct));
        Assert.Empty(await saved.Audit.ToListAsync(Ct));
    }

    [Fact]
    public async Task A_shared_store_outage_is_repaired_without_extending_the_audited_expiry()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var fixture = new TenantPluginFixture(clock);
        await fixture.InitializeAsync(Ct);
        var store = new Store(clock) { FailActivation = true };
        var service = Service(fixture, store);
        var issued = await service.IssueAsync(Operator, Key(), "INC-123", 10, Ct);
        Assert.Equal(ContentGrantStatus.Unavailable, issued.Status);
        Assert.False(issued.Envelope.Active);
        clock.Advance(TimeSpan.FromMinutes(2));
        store.FailActivation = false;

        var repaired = await Service(fixture, store).ReadAsync(Operator, Key(), Ct);

        Assert.True(repaired.Active);
        Assert.Equal(issued.Envelope.Grant, repaired.Grant);
        Assert.Equal(issued.Envelope.Grant!.ExpiresAt, (await store.ReadAsync(Key(), Ct))!.ExpiresAt);
        var conflict = await service.IssueAsync(Operator, Key(), "INC-456", 60, Ct);
        Assert.Equal(ContentGrantStatus.Conflict, conflict.Status);
        Assert.Equal(issued.Envelope.Grant, conflict.Envelope.Grant);
    }

    [Fact]
    public async Task Concurrent_replicas_cannot_create_two_open_grants_for_one_session()
    {
        await using var fixture = new TenantPluginFixture();
        await fixture.InitializeAsync(Ct);
        var store = new Store(fixture.Clock);
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(
            () => Service(fixture, store).IssueAsync(Operator, Key(), "INC-123", 10, Ct), Ct)));

        Assert.Single(results, result => result.Status == ContentGrantStatus.Created);
        Assert.Equal(7, results.Count(result => result.Status == ContentGrantStatus.Conflict));
        await using var db = await fixture.Db.CreateDbContextAsync(Ct);
        Assert.Single(await db.ContentAccessGrants.ToListAsync(Ct));
        Assert.True(AuditChain.Verify(await db.Audit.OrderBy(row => row.Id).ToListAsync(Ct)).Intact);
    }

    [Fact]
    public async Task Shared_permission_without_matching_durable_evidence_or_with_changed_expiry_is_denied()
    {
        await using var fixture = new TenantPluginFixture();
        await fixture.InitializeAsync(Ct);
        var store = new Store(fixture.Clock);
        var service = Service(fixture, store);
        await store.ActivateAsync(new(Key(), "invented", fixture.Clock.GetUtcNow().AddMinutes(10)), Ct);
        Assert.False(await service.IsAllowedAsync(Operator, Key(), Ct));
        await store.RevokeAsync(Key(), "invented", fixture.Clock.GetUtcNow().AddMinutes(10), Ct);
        var grant = (await service.IssueAsync(Operator, Key(), "INC-123", 10, Ct)).Envelope.Grant!;
        store.ForcePermission(new(Key(), grant.Id.ToString(CultureInfo.InvariantCulture), grant.ExpiresAt.AddMinutes(1)));
        Assert.False(await service.IsAllowedAsync(Operator, Key(), Ct));
        store.FailRead = true;
        Assert.False(await service.IsAllowedAsync(Operator, Key(), Ct));
    }

    [Fact]
    public async Task Ending_is_not_acknowledged_until_revocation_and_end_audit_commit_and_cannot_be_reactivated()
    {
        await using var fixture = new TenantPluginFixture();
        await fixture.InitializeAsync(Ct);
        var store = new Store(fixture.Clock);
        var service = Service(fixture, store);
        var issued = await service.IssueAsync(Operator, Key(), "INC-123", 10, Ct);
        var grant = issued.Envelope.Grant!;
        store.FailRevoke = true;

        var pending = await service.EndAsync(Operator, Key(), grant.Id, Ct);

        Assert.Equal(ContentGrantStatus.Unavailable, pending.Status);
        Assert.NotNull(pending.Envelope.Grant!.EndRequestedAt);
        Assert.Null(pending.Envelope.Grant.EndedAt);
        Assert.False(await service.IsAllowedAsync(Operator, Key(), Ct));
        var activationCount = store.Activations;
        Assert.False((await service.ReadAsync(Operator, Key(), Ct)).Active);
        Assert.Equal(activationCount, store.Activations);
        store.FailRevoke = false;
        var ended = await Service(fixture, store).EndAsync(Operator, Key(), grant.Id, Ct);
        Assert.Equal(ContentGrantStatus.Ended, ended.Status);
        Assert.NotNull(ended.Envelope.Grant!.EndedAt);
        Assert.Null(await store.ReadAsync(Key(), Ct));
        Assert.False(await store.ActivateAsync(new(Key(), grant.Id.ToString(CultureInfo.InvariantCulture), grant.ExpiresAt), Ct));
        await service.EndAsync(Operator, Key(), grant.Id, Ct);
        await using var db = await fixture.Db.CreateDbContextAsync(Ct);
        Assert.Equal([ContentAccessGrants.StartedKind, ContentAccessGrants.EndRequestedKind, ContentAccessGrants.EndedKind],
            (await db.Audit.OrderBy(row => row.Id).ToListAsync(Ct)).Select(row => row.Kind));
    }

    [Fact]
    public async Task A_failed_end_audit_keeps_a_retryable_row_and_a_revoked_permission()
    {
        await using var fixture = new TenantPluginFixture();
        await fixture.InitializeAsync(Ct);
        var store = new Store(fixture.Clock);
        var service = Service(fixture, store);
        var id = (await service.IssueAsync(Operator, Key(), "INC-123", 10, Ct)).Envelope.Grant!.Id;
        await using (var db = await fixture.Db.CreateDbContextAsync(Ct))
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER deny_end_audit BEFORE INSERT ON "Audit"
                WHEN NEW."Kind" = 'operator.content-access.end' BEGIN SELECT RAISE(ABORT, 'fixture failure'); END;
                """, Ct);

        await Assert.ThrowsAsync<DbUpdateException>(() => service.EndAsync(Operator, Key(), id, Ct));

        Assert.Null(await store.ReadAsync(Key(), Ct));
        await using (var db = await fixture.Db.CreateDbContextAsync(Ct))
        {
            var row = await db.ContentAccessGrants.SingleAsync(Ct);
            Assert.NotNull(row.EndRequestedAt);
            Assert.Null(row.EndedAt);
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER deny_end_audit", Ct);
        }
        await service.ReconcileAsync(Ct);
        await using var saved = await fixture.Db.CreateDbContextAsync(Ct);
        Assert.NotNull((await saved.ContentAccessGrants.SingleAsync(Ct)).EndedAt);
        Assert.True(AuditChain.Verify(await saved.Audit.OrderBy(row => row.Id).ToListAsync(Ct)).Intact);
    }

    [Fact]
    public async Task Expiry_revokes_without_HTTP_and_reconciliation_records_the_original_end_time()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var fixture = new TenantPluginFixture(clock);
        await fixture.InitializeAsync(Ct);
        var store = new Store(clock);
        var service = Service(fixture, store);
        var issued = await service.IssueAsync(Operator, Key(), "INC-123", 1, Ct);
        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Null(await store.ReadAsync(Key(), Ct));
        Assert.False(await service.IsAllowedAsync(Operator, Key(), Ct));

        await service.ReconcileAsync(Ct);

        var ended = await service.ReadAsync(Operator, Key(), Ct);
        Assert.False(ended.Active);
        Assert.Equal(issued.Envelope.Grant!.ExpiresAt, ended.Grant!.EndedAt);
        await using var db = await fixture.Db.CreateDbContextAsync(Ct);
        Assert.Equal("expired", (await db.Audit.OrderBy(row => row.Id).LastAsync(Ct)).Outcome);
    }

    [Theory]
    [InlineData("new-session", "operator", "firm-a")]
    [InlineData(Sid, "other-operator", "firm-a")]
    [InlineData(Sid, "operator", "firm-b")]
    public async Task A_grant_cannot_be_read_or_ended_by_another_session_actor_or_tenant(string sid, string user, string tenant)
    {
        await using var fixture = new TenantPluginFixture();
        await fixture.InitializeAsync(Ct);
        var store = new Store(fixture.Clock);
        var service = Service(fixture, store);
        var id = (await service.IssueAsync(Operator, Key(), "INC-123", 10, Ct)).Envelope.Grant!.Id;
        var other = new Principal(user, TenantId.Firm(tenant), Role.PLATFORM_ADMIN);
        var key = Key(sid, other);

        Assert.Null((await service.ReadAsync(other, key, Ct)).Grant);
        Assert.False(await service.IsAllowedAsync(other, key, Ct));
        Assert.Equal(ContentGrantStatus.NotFound, (await service.EndAsync(other, key, id, Ct)).Status);
        Assert.True(await service.IsAllowedAsync(Operator, Key(), Ct));
    }

    [Theory]
    [InlineData(Role.USER)]
    [InlineData(Role.READ_ONLY)]
    [InlineData(Role.TENANT_ADMIN)]
    public async Task Role_claims_alone_cannot_grant_content_to_non_operators(Role role)
    {
        await using var fixture = new TenantPluginFixture();
        await fixture.InitializeAsync(Ct);
        var actor = new Principal("alice", TenantId.Firm("firm-a"), role);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service(fixture, new Store(fixture.Clock))
            .IssueAsync(actor, Key(actor: actor), "INC-123", 10, Ct));
    }

    [Theory]
    [InlineData("ticket with message content", 10)]
    [InlineData("https://user:password@host", 10)]
    [InlineData("INC-1?secret=value", 10)]
    [InlineData("x", 10)]
    [InlineData("INC-123", 0)]
    [InlineData("INC-123", 61)]
    public async Task Unbounded_or_content_bearing_requests_never_create_a_grant(string reason, int minutes)
    {
        await using var fixture = new TenantPluginFixture();
        await fixture.InitializeAsync(Ct);
        Assert.Equal(ContentGrantStatus.Invalid, (await Service(fixture, new Store(fixture.Clock))
            .IssueAsync(Operator, Key(), reason, minutes, Ct)).Status);
        await using var db = await fixture.Db.CreateDbContextAsync(Ct);
        Assert.Empty(await db.ContentAccessGrants.ToListAsync(Ct));
        Assert.Empty(await db.Audit.ToListAsync(Ct));
    }

    [Fact]
    public async Task Core_HTTP_lifecycle_ignores_tenant_and_permission_spoofing_and_returns_safe_DTOs()
    {
        var store = new Store(TimeProvider.System);
        using var api = Api(store);
        using var client = OperatorClient(api);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/platform/content-access?tenantId=firm-b")
        { Content = JsonContent.Create(new { reason = "INC-123", durationMinutes = 10, tenantId = "firm-b", contentAccess = true }) };
        request.Headers.Add("X-Tenant-Id", "firm-b");

        var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.True(body.GetProperty("active").GetBoolean());
        var grant = body.GetProperty("grant");
        Assert.Equal(["endRequestedAt", "endedAt", "expiresAt", "id", "operatorId", "reason", "startedAt"],
            grant.EnumerateObject().Select(property => property.Name).Order());
        Assert.DoesNotContain(Sid, body.GetRawText());
        Assert.DoesNotContain(Key(), body.GetRawText());
        await using var db = api.Services.GetRequiredService<IDbContextFactory<MafDbContext>>().CreateDbContext();
        Assert.Equal("firm-a", (await db.ContentAccessGrants.SingleAsync(Ct)).TenantId);
        var id = grant.GetProperty("id").GetInt64();
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/platform/content-access",
            new { reason = "INC-456", durationMinutes = 60 }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/platform/content-access/{id}/end", null, Ct)).StatusCode);
        Assert.False((await client.GetFromJsonAsync<JsonElement>("/api/platform/content-access", Ct)).GetProperty("active").GetBoolean());
    }

    [Fact]
    public async Task HTTP_end_returns_503_while_revocation_is_pending()
    {
        var store = new Store(TimeProvider.System);
        using var api = Api(store);
        using var client = OperatorClient(api);
        var issued = await client.PostAsJsonAsync("/api/platform/content-access", new { reason = "INC-123", durationMinutes = 10 }, Ct);
        var body = await issued.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var id = body.GetProperty("grant").GetProperty("id").GetInt64();
        store.FailRevoke = true;
        var response = await client.PostAsync($"/api/platform/content-access/{id}/end", null, Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var ending = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(JsonValueKind.Null, ending.GetProperty("grant").GetProperty("endedAt").ValueKind);
        store.FailRevoke = false;
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/platform/content-access/{id}/end", null, Ct)).StatusCode);
    }

    [Fact]
    public async Task HTTP_core_routes_require_authentication()
    {
        using var api = Api(new Store(TimeProvider.System));
        using var client = api.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/platform/content-access", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/content-access", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/platform/content-access",
            new { reason = "INC-123", durationMinutes = 10 }, Ct)).StatusCode);
    }

    [Theory]
    [InlineData(Role.USER)]
    [InlineData(Role.READ_ONLY)]
    [InlineData(Role.TENANT_ADMIN)]
    public async Task HTTP_platform_routes_require_the_operator_role(Role role)
    {
        using var api = Api(new Store(TimeProvider.System));
        using var client = api.ClientFor("alice", "firm-a", role);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/platform/content-access", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/platform/content-access",
            new { reason = "INC-123", durationMinutes = 10 }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/platform/content-access/1/end", null, Ct)).StatusCode);
    }

    [Theory]
    [InlineData(Role.USER)]
    [InlineData(Role.READ_ONLY)]
    [InlineData(Role.PLATFORM_ADMIN)]
    public async Task HTTP_tenant_reader_requires_the_tenant_admin_role(Role role)
    {
        using var api = Api(new Store(TimeProvider.System));
        using var client = api.ClientFor("alice", "firm-a", role);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/content-access", Ct)).StatusCode);
    }

    [Fact]
    public async Task Tenant_reader_pages_fifty_grants_and_cannot_be_redirected_by_request_tenant()
    {
        using var api = Api(new Store(TimeProvider.System));
        var service = api.Services.GetRequiredService<ContentAccessGrants>();
        foreach (var index in Enumerable.Range(0, 51))
            await service.IssueAsync(Operator, Key($"session-{index}"), "INC-123", 10, Ct);
        var foreign = new Principal("foreign", TenantId.Firm("firm-b"), Role.PLATFORM_ADMIN);
        await service.IssueAsync(foreign, Key(actor: foreign), "FOREIGN-123", 10, Ct);
        using var client = api.ClientFor("alice", "firm-a", Role.TENANT_ADMIN);
        var first = await client.GetFromJsonAsync<JsonElement>("/api/admin/content-access?tenantId=firm-b&limit=1000", Ct);
        var cursor = first.GetProperty("nextCursor").GetInt64();
        var second = await client.GetFromJsonAsync<JsonElement>($"/api/admin/content-access?before={cursor}", Ct);
        Assert.Equal(50, first.GetProperty("grants").GetArrayLength());
        Assert.Single(second.GetProperty("grants").EnumerateArray());
        Assert.DoesNotContain("FOREIGN-123", first.GetRawText());
        Assert.Equal(JsonValueKind.Null, second.GetProperty("nextCursor").ValueKind);
        var ids = first.GetProperty("grants").EnumerateArray().Concat(second.GetProperty("grants").EnumerateArray())
            .Select(row => row.GetProperty("id").GetInt64()).ToArray();
        Assert.Equal(51, ids.Distinct().Count());
        Assert.Equal(ids.OrderDescending(), ids);
    }

    private static ApiFactory Api(Store store) => new(ApiFactory.ProceduralModel())
    {
        InstalledPlugins = [], BootstrapTenantPlugins = false,
        ConfigureTestServices = services => services.AddSingleton<IBreakGlassPermissionStore>(store),
    };
    private static HttpClient OperatorClient(ApiFactory api)
    {
        var client = api.CreateClient();
        var token = DevJwt.Issue(new AuthOptions(), Operator.UserId, Operator.TenantId, Operator.Role, sessionId: Sid).Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private sealed class Store(TimeProvider clock) : IBreakGlassPermissionStore
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, ContentPermission> _active = [];
        private readonly HashSet<string> _ended = [];
        public bool FailActivation { get; set; }
        public bool FailRevoke { get; set; }
        public bool FailRead { get; set; }
        public int Activations { get; private set; }
        public Func<ContentPermission, Task>? BeforeActivate { get; init; }
        public async Task<bool> ActivateAsync(ContentPermission permission, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (BeforeActivate is not null) await BeforeActivate(permission);
            lock (_gate)
            {
                Activations++;
                if (FailActivation) throw new InvalidOperationException("fixture outage");
                if (_ended.Contains(permission.GrantId) || permission.ExpiresAt <= clock.GetUtcNow()) return false;
                if (_active.TryGetValue(permission.SessionKey, out var existing) && existing != permission) return false;
                _active[permission.SessionKey] = permission;
                return true;
            }
        }
        public Task RevokeAsync(string sessionKey, string grantId, DateTimeOffset expiresAt, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (FailRevoke) throw new InvalidOperationException("fixture outage");
                _ended.Add(grantId);
                if (_active.TryGetValue(sessionKey, out var existing) && existing.GrantId == grantId) _active.Remove(sessionKey);
            }
            return Task.CompletedTask;
        }
        public Task<ContentPermission?> ReadAsync(string sessionKey, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (FailRead) throw new InvalidOperationException("fixture outage");
                return Task.FromResult(_active.TryGetValue(sessionKey, out var value) && value.ExpiresAt > clock.GetUtcNow() ? value : null);
            }
        }
        public void ForcePermission(ContentPermission permission) { lock (_gate) _active[permission.SessionKey] = permission; }
    }
}
