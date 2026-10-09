using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Maf.Lab.Api.Agent;
using Maf.Lab.Api.Compliance;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Configuration;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Retrieval.Auth;
using Maf.Lab.TestSupport;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Maf.Lab.Tests;

public sealed class OperatorSessionAuditTests
{
    private const string Issuer = "https://company.identity.invalid/realms/lab";
    private const string Session = "sensitive-native-session-id";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static Principal Operator(string user = "operator", string tenant = "firm-a") =>
        new(user, TenantId.Firm(tenant), Role.PLATFORM_ADMIN);

    private static OperatorSessionAudit Recorder(TenantPluginFixture fixture) => new(fixture.Db,
        new ToolAudit(fixture.Db, NullLogger<ToolAudit>.Instance, fixture.Clock), fixture.Clock);

    [Fact]
    public async Task The_first_entry_is_chained_and_a_repeated_session_is_durable_across_service_instances()
    {
        await using var fixture = new TenantPluginFixture();
        await fixture.InitializeAsync(Ct);

        Assert.True(await Recorder(fixture).RecordAsync(Operator(), Issuer, Session, Ct));
        Assert.False(await Recorder(fixture).RecordAsync(Operator(), Issuer, Session, Ct));

        await using var db = await fixture.Db.CreateDbContextAsync(Ct);
        var marker = Assert.Single(await db.OperatorSessions.AsNoTracking().ToListAsync(Ct));
        var row = Assert.Single(await db.Audit.AsNoTracking().ToListAsync(Ct));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new[] { Issuer, Session, "operator", "firm-a" })))), marker.SessionKey, ignoreCase: true);
        Assert.Equal("operator", row.PrincipalId);
        Assert.Equal("firm-a", row.TenantId);
        Assert.Equal(AuditKinds.OperatorEnter, row.Kind);
        Assert.Equal(AuditKinds.OperatorEnter, row.ToolName);
        Assert.Equal("entered", row.Outcome);
        Assert.DoesNotContain(Session, row.Arguments);
        Assert.True(AuditChain.Verify([row]).Intact);
    }

    [Theory]
    [InlineData("new-session", Issuer, "operator", "firm-a")]
    [InlineData(Session, "https://other.identity.invalid/realms/lab", "operator", "firm-a")]
    [InlineData(Session, Issuer, "other-operator", "firm-a")]
    [InlineData(Session, Issuer, "operator", "firm-b")]
    public async Task Session_issuer_actor_and_tenant_each_distinguish_an_entry(string session, string issuer, string user, string tenant)
    {
        await using var fixture = new TenantPluginFixture();
        await fixture.InitializeAsync(Ct);
        var recorder = Recorder(fixture);
        await recorder.RecordAsync(Operator(), Issuer, Session, Ct);

        Assert.True(await recorder.RecordAsync(Operator(user, tenant), issuer, session, Ct));

        await using var db = await fixture.Db.CreateDbContextAsync(Ct);
        Assert.Equal(2, await db.OperatorSessions.CountAsync(Ct));
        Assert.True(AuditChain.Verify(await db.Audit.OrderBy(row => row.Id).ToListAsync(Ct)).Intact);
    }

    [Fact]
    public async Task Concurrent_core_instances_record_one_entry_in_a_shared_database()
    {
        await using var fixture = new TenantPluginFixture();
        await fixture.InitializeAsync(Ct);
        var first = Recorder(fixture);
        var second = Recorder(fixture);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(index => Task.Run(
            () => (index % 2 == 0 ? first : second).RecordAsync(Operator(), Issuer, Session, Ct), Ct)));

        Assert.Single(results, result => result);
        await using var db = await fixture.Db.CreateDbContextAsync(Ct);
        Assert.Equal(1, await db.OperatorSessions.CountAsync(Ct));
        var row = Assert.Single(await db.Audit.ToListAsync(Ct));
        Assert.True(AuditChain.Verify([row]).Intact);
    }

    [Fact]
    public async Task Audit_append_failure_rolls_back_the_marker_and_a_retry_records_the_entry()
    {
        await using var fixture = new TenantPluginFixture();
        await fixture.InitializeAsync(Ct);
        await using (var setup = await fixture.Db.CreateDbContextAsync(Ct))
            await setup.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER deny_operator_audit BEFORE INSERT ON "Audit"
                WHEN NEW."Kind" = 'operator.enter' BEGIN SELECT RAISE(ABORT, 'fixture append failure'); END;
                """, Ct);

        await Assert.ThrowsAsync<DbUpdateException>(() => Recorder(fixture).RecordAsync(Operator(), Issuer, Session, Ct));

        await using (var db = await fixture.Db.CreateDbContextAsync(Ct))
        {
            Assert.Equal(0, await db.OperatorSessions.CountAsync(Ct));
            Assert.Equal(0, await db.Audit.CountAsync(Ct));
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER deny_operator_audit", Ct);
        }
        Assert.True(await Recorder(fixture).RecordAsync(Operator(), Issuer, Session, Ct));
        await using var saved = await fixture.Db.CreateDbContextAsync(Ct);
        Assert.Equal(1, await saved.OperatorSessions.CountAsync(Ct));
        Assert.True(AuditChain.Verify(await saved.Audit.ToListAsync(Ct)).Intact);
    }

    [Theory]
    [InlineData(Role.USER)]
    [InlineData(Role.READ_ONLY)]
    [InlineData(Role.TENANT_ADMIN)]
    public async Task The_core_recorder_refuses_non_operator_roles(Role role)
    {
        await using var fixture = new TenantPluginFixture();
        await fixture.InitializeAsync(Ct);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Recorder(fixture).RecordAsync(
            new Principal("alice", TenantId.Firm("firm-a"), role), Issuer, Session, Ct));

        await using var db = await fixture.Db.CreateDbContextAsync(Ct);
        Assert.Empty(await db.OperatorSessions.ToListAsync(Ct));
        Assert.Empty(await db.Audit.ToListAsync(Ct));
    }

    [Theory]
    [InlineData("", Session)]
    [InlineData(" ", Session)]
    [InlineData(Issuer, "")]
    [InlineData(Issuer, " ")]
    public async Task Entries_without_an_issuer_or_session_are_not_persisted(string issuer, string session)
    {
        await using var fixture = new TenantPluginFixture();
        await fixture.InitializeAsync(Ct);

        await Assert.ThrowsAsync<ArgumentException>(() => Recorder(fixture).RecordAsync(Operator(), issuer, session, Ct));

        await using var db = await fixture.Db.CreateDbContextAsync(Ct);
        Assert.Empty(await db.OperatorSessions.ToListAsync(Ct));
        Assert.Empty(await db.Audit.ToListAsync(Ct));
    }

    [Fact]
    public async Task Shared_is_never_an_operator_target_tenant()
    {
        await using var fixture = new TenantPluginFixture();
        await fixture.InitializeAsync(Ct);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Recorder(fixture).RecordAsync(
            new Principal("operator", TenantId.Shared, Role.PLATFORM_ADMIN), Issuer, Session, Ct));

        await using var db = await fixture.Db.CreateDbContextAsync(Ct);
        Assert.Empty(await db.OperatorSessions.ToListAsync(Ct));
    }

    [Fact]
    public async Task The_additive_initializer_adds_the_core_session_table_without_losing_existing_audit()
    {
        await using var fixture = new TenantPluginFixture();
        await fixture.InitializeAsync(Ct);
        await new ToolAudit(fixture.Db, NullLogger<ToolAudit>.Instance, fixture.Clock).RecordAsync(
            new AuditEntry(TenantPluginFixture.User, null, null, "search_documents", "", "ok", 0), Ct);
        await using (var previous = await fixture.Db.CreateDbContextAsync(Ct))
            await previous.Database.ExecuteSqlRawAsync("DROP TABLE \"OperatorSessions\"", Ct);

        await fixture.InitializeAsync(Ct);
        await fixture.InitializeAsync(Ct);
        Assert.True(await Recorder(fixture).RecordAsync(Operator(), Issuer, Session, Ct));

        await using var db = await fixture.Db.CreateDbContextAsync(Ct);
        Assert.Equal(1, await db.OperatorSessions.CountAsync(Ct));
        var rows = await db.Audit.OrderBy(row => row.Id).ToListAsync(Ct);
        Assert.Equal([AuditKinds.Tool, AuditKinds.OperatorEnter], rows.Select(row => row.Kind));
        Assert.True(AuditChain.Verify(rows).Intact);
    }

    [Theory]
    [InlineData("openid organization:firm-a", true)]
    [InlineData("organization:firm-a domain-claims profile", true)]
    [InlineData("openid", false)]
    [InlineData("organization", false)]
    [InlineData("organization:*", false)]
    [InlineData("organization:firm-b", false)]
    [InlineData("organization:firm-a organization:firm-b", false)]
    [InlineData("organization:firm-a organization:*", false)]
    [InlineData("organization:firm-a organization", false)]
    [InlineData("organization:firm-a organization:firm-a", false)]
    public void Company_operator_sessions_require_one_exact_selected_organization_scope(string scope, bool expected)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sid", Session), new Claim("scope", scope)], "Bearer"));

        var accepted = OperatorSessionAudit.TryReadSession(user, Operator(), true, out var session);

        Assert.Equal(expected, accepted);
        Assert.Equal(expected ? Session : "", session);
    }

    [Theory]
    [InlineData("missing-sid")]
    [InlineData("empty-sid")]
    [InlineData("many-sid")]
    [InlineData("number-sid")]
    [InlineData("many-scope")]
    [InlineData("array-scope")]
    public void Malformed_session_and_scope_claims_are_rejected(string fault)
    {
        var claims = SessionClaims(fault);
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));

        Assert.False(OperatorSessionAudit.TryReadSession(user, Operator(), true, out var session));
        Assert.Equal("", session);
    }

    private static Claim[] SessionClaims(string fault) => fault switch
    {
        "missing-sid" => [new("scope", "organization:firm-a")],
        "empty-sid" => [new("sid", " "), new("scope", "organization:firm-a")],
        "many-sid" => [new("sid", Session), new("sid", "another"), new("scope", "organization:firm-a")],
        "number-sid" => [new("sid", "123", ClaimValueTypes.Integer), new("scope", "organization:firm-a")],
        "many-scope" => [new("sid", Session), new("scope", "organization:firm-a"), new("scope", "openid")],
        _ => [new("sid", Session), new("scope", "[\"organization:firm-a\"]", "JSON_ARRAY")],
    };

    [Fact]
    public void A_development_operator_session_does_not_require_company_organization_scopes()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sid", Session)], "Bearer"));

        Assert.True(OperatorSessionAudit.TryReadSession(user, Operator(), false, out var session));
        Assert.Equal(Session, session);
    }

    [Fact]
    public void An_unauthenticated_identity_cannot_supply_an_operator_session()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sid", Session), new Claim("scope", "organization:firm-a")]));

        Assert.False(OperatorSessionAudit.TryReadSession(user, Operator(), true, out var session));
        Assert.Equal("", session);
    }

    [Fact]
    public async Task The_operator_reader_requires_authentication()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel()) { InstalledPlugins = [], BootstrapTenantPlugins = false };
        using var client = api.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/operator-audit", Ct)).StatusCode);
    }

    [Fact]
    public async Task Refreshed_company_tokens_with_one_sid_record_once_and_a_new_login_records_again()
    {
        using var rsa = RSA.Create(2048);
        using var api = CompanyApi(rsa);
        using var client = api.CreateClient();

        Authorize(client, CompanyToken(rsa, session: Session));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/platform/plugins", Ct)).StatusCode);
        Authorize(client, CompanyToken(rsa, session: Session));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/platform/plugins", Ct)).StatusCode);
        Authorize(client, CompanyToken(rsa, session: "new-login"));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/platform/plugins", Ct)).StatusCode);

        var rows = await Entries(api);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Equal("firm-a", row.TenantId));
        Assert.True(AuditChain.Verify(rows).Intact);
        Assert.DoesNotContain(api.Logs.Messages, message => message.Contains(Session, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("missing-sid")]
    [InlineData("empty-sid")]
    [InlineData("number-sid")]
    [InlineData("array-sid")]
    [InlineData("single-array-sid")]
    [InlineData("missing-scope")]
    [InlineData("bare-organization")]
    [InlineData("wildcard-organization")]
    [InlineData("foreign-organization")]
    [InlineData("many-organization-scopes")]
    [InlineData("array-scope")]
    [InlineData("single-array-scope")]
    [InlineData("missing-organization")]
    public async Task Invalid_company_operator_sessions_cannot_dispatch_or_append_an_entry(string fault)
    {
        using var rsa = RSA.Create(2048);
        using var api = CompanyApi(rsa);
        using var client = api.CreateClient();
        Authorize(client, CompanyToken(rsa, fault: fault));

        var response = await client.PutAsJsonAsync("/api/platform/plugins/absent", new { allowed = true, enabled = true }, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, challenge => challenge.Scheme == "Bearer");
        Assert.Empty(await Entries(api));
        await using var db = Db(api);
        Assert.Empty(await db.PluginEntitlements.ToListAsync(Ct));
        Assert.Empty(await db.OperatorSessions.ToListAsync(Ct));
    }

    [Fact]
    public async Task Spoofed_request_tenant_inputs_cannot_redirect_a_company_operator_entry()
    {
        using var rsa = RSA.Create(2048);
        using var api = CompanyApi(rsa);
        using var client = api.CreateClient();
        Authorize(client, CompanyToken(rsa));
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/platform/plugins/absent?tenantId=firm-b")
        {
            Content = JsonContent.Create(new { tenantId = "firm-b", allowed = true, enabled = true }),
        };
        request.Headers.Add("X-Tenant-Id", "firm-b");

        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(request, Ct)).StatusCode);

        var entry = Assert.Single(await Entries(api));
        Assert.Equal("firm-a", entry.TenantId);
        Assert.Equal("operator", entry.PrincipalId);
    }

    [Fact]
    public async Task An_audit_failure_prevents_API_dispatch_and_a_retry_enters_once()
    {
        using var rsa = RSA.Create(2048);
        using var api = CompanyApi(rsa);
        using var client = api.CreateClient();
        Authorize(client, CompanyToken(rsa));
        await using (var setup = Db(api))
            await setup.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER deny_operator_audit BEFORE INSERT ON "Audit"
                WHEN NEW."Kind" = 'operator.enter' BEGIN SELECT RAISE(ABORT, 'fixture append failure'); END;
                """, Ct);

        Assert.Equal(HttpStatusCode.InternalServerError, (await client.GetAsync("/api/platform/plugins", Ct)).StatusCode);

        Assert.Empty(await Entries(api));
        await using (var db = Db(api))
        {
            Assert.Empty(await db.OperatorSessions.ToListAsync(Ct));
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER deny_operator_audit", Ct);
        }
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/platform/plugins", Ct)).StatusCode);
        Assert.Single(await Entries(api));
    }

    [Fact]
    public async Task An_entry_is_recorded_even_when_the_selected_route_refuses_an_operator()
    {
        using var rsa = RSA.Create(2048);
        using var api = CompanyApi(rsa);
        using var client = api.CreateClient();
        Authorize(client, CompanyToken(rsa));

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/operator-audit", Ct)).StatusCode);

        Assert.Single(await Entries(api));
    }

    [Theory]
    [InlineData("USER")]
    [InlineData("READ_ONLY")]
    [InlineData("TENANT_ADMIN")]
    public async Task Ordinary_company_users_never_create_operator_entries(string role)
    {
        using var rsa = RSA.Create(2048);
        using var api = CompanyApi(rsa);
        using var client = api.CreateClient();
        Authorize(client, CompanyToken(rsa, role: role, fault: "missing-sid"));

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/platform/plugins", Ct)).StatusCode);

        Assert.Empty(await Entries(api));
    }

    [Fact]
    public async Task Two_API_hosts_deduplicate_one_company_session_in_the_shared_store()
    {
        using var rsa = RSA.Create(2048);
        var directory = Directory.CreateTempSubdirectory("maf-operator-replicas-").FullName;
        using var first = CompanyApi(rsa, directory);
        using var second = CompanyApi(rsa, directory);
        using var clientA = first.CreateClient();
        using var clientB = second.CreateClient();
        Authorize(clientA, CompanyToken(rsa));
        Authorize(clientB, CompanyToken(rsa));

        var responses = await Task.WhenAll(clientA.GetAsync("/api/platform/plugins", Ct), clientB.GetAsync("/api/platform/plugins", Ct));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        var row = Assert.Single(await Entries(first));
        Assert.True(AuditChain.Verify([row]).Intact);
        await using var db = Db(second);
        Assert.Equal(1, await db.OperatorSessions.CountAsync(Ct));
    }

    [Fact]
    public async Task The_core_only_tenant_reader_ignores_spoofed_tenant_input_and_exposes_only_entry_identifiers()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel()) { InstalledPlugins = [], BootstrapTenantPlugins = false };
        var recorder = api.Services.GetRequiredService<OperatorSessionAudit>();
        await recorder.RecordAsync(Operator(), Issuer, Session, Ct);
        await recorder.RecordAsync(Operator("other", "firm-b"), Issuer, "foreign-secret-sid", Ct);
        await api.Services.GetRequiredService<ToolAudit>().RecordAsync(
            new AuditEntry(TenantPluginFixture.User, null, null, "search_documents", "docId=secret-doc", "ok", 0), Ct);
        using var client = api.ClientFor("alice", "firm-a", Role.TENANT_ADMIN);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/operator-audit?tenantId=firm-b&firmId=firm-b&limit=1000")
        {
            Content = JsonContent.Create(new { tenantId = "firm-b" }),
        };
        request.Headers.Add("X-Tenant-Id", "firm-b");

        var response = await client.SendAsync(request, Ct);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var action = Assert.Single(body.GetProperty("actions").EnumerateArray());
        Assert.Equal(["at", "id", "operatorId"], action.EnumerateObject().Select(property => property.Name).Order());
        Assert.Equal("operator", action.GetProperty("operatorId").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("nextCursor").ValueKind);
        var json = body.GetRawText();
        Assert.DoesNotContain(Session, json);
        Assert.DoesNotContain("foreign-secret-sid", json);
        Assert.DoesNotContain("secret-doc", json);
        Assert.DoesNotContain(api.Logs.Messages, message => message.Contains(Session, StringComparison.Ordinal));
        Assert.DoesNotContain(api.Logs.Messages, message => message.Contains(client.DefaultRequestHeaders.Authorization!.Parameter!, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(Role.USER)]
    [InlineData(Role.READ_ONLY)]
    [InlineData(Role.PLATFORM_ADMIN)]
    public async Task Only_the_tenant_admin_can_read_operator_entries(Role role)
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel()) { InstalledPlugins = [], BootstrapTenantPlugins = false };
        using var client = api.ClientFor("alice", "firm-a", role);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/operator-audit", Ct)).StatusCode);
    }

    [Fact]
    public async Task Operator_entries_use_a_fixed_page_size_and_cursor_without_crossing_tenants()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel()) { InstalledPlugins = [], BootstrapTenantPlugins = false };
        var recorder = api.Services.GetRequiredService<OperatorSessionAudit>();
        await Task.WhenAll(Enumerable.Range(0, 51).Select(index => recorder.RecordAsync(Operator(), Issuer, $"session-{index}", Ct)));
        await recorder.RecordAsync(Operator("foreign", "firm-b"), Issuer, "foreign-session", Ct);
        using var client = api.ClientFor("alice", "firm-a", Role.TENANT_ADMIN);

        var first = await client.GetFromJsonAsync<JsonElement>("/api/admin/operator-audit?limit=1000", Ct);
        var cursor = first.GetProperty("nextCursor").GetInt64();
        var second = await client.GetFromJsonAsync<JsonElement>($"/api/admin/operator-audit?before={cursor}", Ct);

        Assert.Equal(50, first.GetProperty("actions").GetArrayLength());
        Assert.Single(second.GetProperty("actions").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, second.GetProperty("nextCursor").ValueKind);
        var ids = first.GetProperty("actions").EnumerateArray().Concat(second.GetProperty("actions").EnumerateArray())
            .Select(action => action.GetProperty("id").GetInt64()).ToArray();
        Assert.Equal(51, ids.Distinct().Count());
        Assert.Equal(ids.OrderDescending(), ids);
        Assert.All(first.GetProperty("actions").EnumerateArray(), action => Assert.Equal("operator", action.GetProperty("operatorId").GetString()));
    }

    private static MafDbContext Db(ApiFactory api) => api.Services.GetRequiredService<IDbContextFactory<MafDbContext>>().CreateDbContext();

    private static async Task<List<AuditRow>> Entries(ApiFactory api)
    {
        await using var db = Db(api);
        return await db.Audit.Where(row => row.Kind == AuditKinds.OperatorEnter).OrderBy(row => row.Id).ToListAsync(Ct);
    }

    private static ApiFactory CompanyApi(RSA rsa, string? directory = null) => new(ApiFactory.ProceduralModel(), dataDir: directory)
    {
        InstalledPlugins = [], BootstrapTenantPlugins = false,
        ExtraSettings = new Dictionary<string, string?> { ["Auth:Authority"] = Issuer },
        ConfigureTestServices = services => services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme,
            options => options.Backchannel = new HttpClient(new Metadata(rsa))),
    };

    private static void Authorize(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static string CompanyToken(RSA rsa, string role = "platform_operator", string session = Session, string? fault = null)
    {
        var claims = new Dictionary<string, object>
        {
            ["sub"] = "operator", ["typ"] = "Bearer", ["jti"] = Guid.NewGuid().ToString(), ["sid"] = session,
            ["scope"] = "openid organization:firm-a domain-claims",
            ["organization"] = new Dictionary<string, object> { ["firm-a"] = new { } },
            ["realm_access"] = new { roles = new[] { role } },
        };
        switch (fault)
        {
            case "missing-sid": claims.Remove("sid"); break;
            case "empty-sid": claims["sid"] = " "; break;
            case "number-sid": claims["sid"] = 123; break;
            case "array-sid": claims["sid"] = new[] { Session, "another" }; break;
            case "single-array-sid": claims["sid"] = new[] { Session }; break;
            case "missing-scope": claims.Remove("scope"); break;
            case "bare-organization": claims["scope"] = "openid organization"; break;
            case "wildcard-organization": claims["scope"] = "openid organization:*"; break;
            case "foreign-organization": claims["scope"] = "openid organization:firm-b"; break;
            case "many-organization-scopes": claims["scope"] = "organization:firm-a organization:firm-b"; break;
            case "array-scope": claims["scope"] = new[] { "organization:firm-a", "openid" }; break;
            case "single-array-scope": claims["scope"] = new[] { "organization:firm-a" }; break;
            case "missing-organization": claims.Remove("organization"); break;
        }
        var now = DateTime.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer, Audience = "api", IssuedAt = now.AddMinutes(-1), NotBefore = now.AddMinutes(-1), Expires = now.AddMinutes(5),
            Claims = JsonSerializer.SerializeToElement(claims).EnumerateObject().ToDictionary(property => property.Name, property => (object)property.Value),
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(rsa) { KeyId = "fixture" }, SecurityAlgorithms.RsaSha256),
        });
    }

    private sealed class Metadata(RSA rsa) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Null(request.Headers.Authorization);
            var parameters = rsa.ExportParameters(false);
            object document = request.RequestUri!.AbsoluteUri == Issuer + "/.well-known/openid-configuration"
                ? new { issuer = Issuer, jwks_uri = Issuer + "/jwks" }
                : new { keys = new[] { new { kty = "RSA", use = "sig", kid = "fixture", alg = "RS256", n = Base64UrlEncoder.Encode(parameters.Modulus!), e = Base64UrlEncoder.Encode(parameters.Exponent!) } } };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(document) });
        }
    }
}
