extern alias service;

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Maf.Lab.Api.Compliance;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Configuration;
using Maf.Lab.Domain.SharedState;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Plugins.Abstractions;
using Maf.Lab.Retrieval.Auth;
using Maf.Lab.Retrieval.Store;
using Maf.Lab.TestSupport;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol;
using ModelContextProtocol.Client;

namespace Maf.Lab.Tests;

/// <summary>Actual HTTP and official MCP boundaries, with SQLite evidence and a shared permission-store seam.</summary>
public sealed class ContentAccessEnforcementTests
{
    private const string Sid = "operator-session-a";
    private const string Conversation = "operator-owned-conversation";
    private const string CompanyIssuer = "https://identity.fixture.invalid/realms/lab";
    private static Principal Operator => new("operator", TenantId.Firm("firm-a"), Role.PLATFORM_ADMIN);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static string Key(Principal? actor = null, string sid = Sid) =>
        OperatorSessionKey.Create(new AuthOptions().Issuer, sid, actor ?? Operator);

    [Theory]
    [InlineData("/api/conversations/operator-owned-conversation")]
    [InlineData("/api/conversations/operator-owned-conversation/pending")]
    [InlineData("/api/fixture/ping")]
    public async Task Operator_content_routes_deny_before_the_handler_even_when_the_operator_owns_the_conversation(string path)
    {
        using var api = Api(new PermissionStore());
        await SeedConversation(api, Operator);
        using var client = Client(api, Operator);

        var response = await client.GetAsync(path, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("private-answer", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Denied_chat_never_calls_the_model_or_the_MCP_tool_source_or_creates_a_turn()
    {
        using var api = Api(new PermissionStore());
        using var client = Client(api, Operator);

        var response = await client.PostAsJsonAsync("/api/chat", new
        {
            runId = "denied-run", messages = new[] { new { id = "message", role = "user", content = "How do I fix a missing fee schedule?" } },
        }, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(api.Chat.Requests);
        Assert.Empty(api.Tools.RequestedDomains);
        Assert.Empty(api.Tools.Invocations);
        await using var db = await api.Services.GetRequiredService<IDbContextFactory<MafDbContext>>().CreateDbContextAsync(Ct);
        Assert.Empty(await db.Turns.ToListAsync(Ct));
    }

    [Fact]
    public async Task The_real_monitor_trace_route_does_not_read_shared_trace_content_without_a_grant()
    {
        var traces = new RecordingTraceStore();
        using var api = Api(new PermissionStore(), monitor: true, traces: traces);
        await api.Runs.SaveAsync(new RunState("owned-run", Conversation, Operator.UserId, "firm-a", "", [],
            RunOutcomes.Running, null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow), Ct);
        using var client = Client(api, Operator);

        var response = await client.GetAsync("/api/runs/owned-run/trace", Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, traces.Reads);
    }

    [Fact]
    public async Task An_audited_grant_allows_the_real_monitor_to_read_the_operators_owned_live_trace()
    {
        var traces = new RecordingTraceStore();
        using var api = Api(new PermissionStore(), monitor: true, traces: traces);
        await api.Runs.SaveAsync(new RunState("owned-run", Conversation, Operator.UserId, "firm-a", "", [],
            RunOutcomes.Running, null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow), Ct);
        await api.Services.GetRequiredService<ContentAccessGrants>().IssueAsync(Operator, Key(), "INC-123", 10, Ct);
        using var client = Client(api, Operator);

        var response = await client.GetAsync("/api/runs/owned-run/trace", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, traces.Reads);
    }

    [Theory]
    [InlineData("/api/me")]
    [InlineData("/api/platform/plugins")]
    [InlineData("/api/platform/content-access")]
    public async Task Configuration_and_grant_status_are_available_without_content_permission(string path)
    {
        using var api = Api(new PermissionStore());
        using var client = Client(api, Operator);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path, Ct)).StatusCode);
    }

    [Theory]
    [InlineData("/api/runs/uninstalled/trace")]
    [InlineData("/api/admin/feedback/chunks?ids=private")]
    public async Task An_absent_optional_content_plugin_remains_not_found_before_the_grant_check(string path)
    {
        using var api = Api(new PermissionStore());
        using var client = Client(api, Operator);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path, Ct)).StatusCode);
    }

    [Fact]
    public async Task A_durably_audited_grant_opens_owned_history_and_ending_it_closes_the_same_session()
    {
        using var api = Api(new PermissionStore());
        await SeedConversation(api, Operator);
        using var client = Client(api, Operator);
        var grants = api.Services.GetRequiredService<ContentAccessGrants>();

        var issued = await grants.IssueAsync(Operator, Key(), "INC-123", 10, Ct);
        var opened = await client.GetAsync($"/api/conversations/{Conversation}", Ct);
        await grants.EndAsync(Operator, Key(), issued.Envelope.Grant!.Id, Ct);
        var closed = await client.GetAsync($"/api/conversations/{Conversation}", Ct);

        Assert.Equal(ContentGrantStatus.Created, issued.Status);
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        Assert.Contains("private-answer", await opened.Content.ReadAsStringAsync(Ct));
        Assert.Equal(HttpStatusCode.Forbidden, closed.StatusCode);
        await using var db = await api.Services.GetRequiredService<IDbContextFactory<MafDbContext>>().CreateDbContextAsync(Ct);
        Assert.Contains(await db.Audit.ToListAsync(Ct), row => row.Kind == ContentAccessGrants.StartedKind);
        Assert.Contains(await db.Audit.ToListAsync(Ct), row => row.Kind == ContentAccessGrants.EndedKind);
    }

    [Theory]
    [InlineData("other-session", "operator", "firm-a")]
    [InlineData(Sid, "another-operator", "firm-a")]
    [InlineData(Sid, "operator", "firm-b")]
    public async Task A_valid_grant_cannot_be_borrowed_by_another_session_actor_or_tenant(string sid, string user, string tenant)
    {
        using var api = Api(new PermissionStore());
        await SeedConversation(api, Operator);
        await api.Services.GetRequiredService<ContentAccessGrants>().IssueAsync(Operator, Key(), "INC-123", 10, Ct);
        using var client = Client(api, new Principal(user, TenantId.Firm(tenant), Role.PLATFORM_ADMIN), sid);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/conversations/{Conversation}", Ct)).StatusCode);
    }

    [Fact]
    public async Task A_grant_preserves_the_normal_owner_check_for_another_users_history()
    {
        using var api = Api(new PermissionStore());
        await SeedConversation(api, Operator with { UserId = "alice" });
        using var client = Client(api, Operator);
        await api.Services.GetRequiredService<ContentAccessGrants>().IssueAsync(Operator, Key(), "INC-123", 10, Ct);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/conversations/{Conversation}", Ct)).StatusCode);
    }

    [Theory]
    [InlineData(Role.USER)]
    [InlineData(Role.TENANT_ADMIN)]
    [InlineData(Role.READ_ONLY)]
    public async Task Normal_tenant_roles_can_still_read_their_own_history_without_a_grant(Role role)
    {
        var actor = Operator with { Role = role };
        using var api = Api(new PermissionStore());
        await SeedConversation(api, actor);
        using var client = Client(api, actor);

        var response = await client.GetAsync($"/api/conversations/{Conversation}", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("private-answer", await response.Content.ReadAsStringAsync(Ct));
    }

    [Theory]
    [InlineData("expiry")]
    [InlineData("revoked")]
    [InlineData("ending")]
    [InlineData("outage")]
    public async Task Expired_revoked_ending_and_unreadable_grants_fail_closed_at_the_actual_API(string fault)
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var store = new PermissionStore(clock);
        using var api = Api(store, clock);
        await SeedConversation(api, Operator);
        var grants = api.Services.GetRequiredService<ContentAccessGrants>();
        var issued = await grants.IssueAsync(Operator, Key(), "INC-123", 1, Ct);
        await InvalidateAsync(fault, store, clock, grants, issued.Envelope.Grant!);
        using var client = Client(api, Operator);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/conversations/{Conversation}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Issuer_signed_permission_claims_do_not_replace_server_side_grant_evidence()
    {
        using var api = Api(new PermissionStore());
        await SeedConversation(api, Operator);
        using var client = api.CreateClient();
        var auth = new AuthOptions();
        var now = DateTime.UtcNow;
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = auth.Issuer, Audience = auth.Audience, IssuedAt = now.AddMinutes(-1), NotBefore = now.AddMinutes(-1), Expires = now.AddMinutes(5),
            Claims = new Dictionary<string, object> { ["sub"] = Operator.UserId, ["tenant_id"] = "firm-a", ["role"] = "PLATFORM_ADMIN",
                ["sid"] = Sid, ["content_access"] = true, ["break_glass"] = true, ["grant_id"] = "123" },
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(auth.SigningKey)), SecurityAlgorithms.HmacSha256),
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/conversations/{Conversation}", Ct)).StatusCode);
    }

    [Theory]
    [InlineData("billing", "search_documents")]
    [InlineData("portfolio", "search_portfolio_documents")]
    [InlineData("code", "search_codebase")]
    public async Task Every_real_MCP_host_allows_operator_discovery_but_denies_content_before_tool_argument_validation(string audience, string tool)
    {
        var ranker = new RecordingRanker();
        var (server, http) = Resource(audience, new PermissionStore(), ranker);
        using var lifetime = server;
        using var client = http;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(Operator, audience));
        await using var mcp = await Connect(client);

        Assert.NotEmpty(await mcp.ListToolsAsync(cancellationToken: Ct));
        var error = await Assert.ThrowsAsync<McpProtocolException>(async () => await mcp.CallToolAsync(tool, new Dictionary<string, object?>(), cancellationToken: Ct));

        Assert.Contains("active break-glass grant", error.Message);
        Assert.Empty(ranker.Actors);
    }

    [Fact]
    public async Task A_grant_published_by_the_actual_API_authorizes_the_real_code_MCP_host_and_preserves_tenant_identity()
    {
        var store = new PermissionStore();
        using var api = Api(store);
        var issued = await api.Services.GetRequiredService<ContentAccessGrants>().IssueAsync(Operator, Key(), "INC-123", 10, Ct);
        var ranker = new RecordingRanker();
        var (server, http) = Resource("code", store, ranker);
        using var lifetime = server;
        using var client = http;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(Operator, "code"));
        await using var mcp = await Connect(client);

        var result = await mcp.CallToolAsync("search_codebase", new Dictionary<string, object?> { ["query"] = "tenant" }, cancellationToken: Ct);
        await api.Services.GetRequiredService<ContentAccessGrants>().EndAsync(Operator, Key(), issued.Envelope.Grant!.Id, Ct);
        var denied = await Assert.ThrowsAsync<McpProtocolException>(async () => await mcp.CallToolAsync("search_codebase",
            new Dictionary<string, object?> { ["query"] = "tenant" }, cancellationToken: Ct));

        Assert.False(result.IsError == true);
        Assert.Equal(Operator, Assert.Single(ranker.Actors));
        Assert.Contains("active break-glass grant", denied.Message);
    }

    [Fact]
    public async Task A_native_company_operator_token_with_selected_organization_and_string_sid_reads_only_its_published_permission()
    {
        using var rsa = RSA.Create(2048);
        var store = new PermissionStore();
        using var api = Api(store);
        var sessionKey = OperatorSessionKey.Create(CompanyIssuer, Sid, Operator);
        await api.Services.GetRequiredService<ContentAccessGrants>().IssueAsync(Operator, sessionKey, "INC-123", 10, Ct);
        var ranker = new RecordingRanker();
        var (server, http) = Resource<service::Maf.Lab.CodeSearch.Program>(store, ranker, rsa);
        using var lifetime = server;
        using var client = http;
        var now = DateTime.UtcNow;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = CompanyIssuer, Audience = "code", IssuedAt = now.AddMinutes(-1), NotBefore = now.AddMinutes(-1), Expires = now.AddMinutes(5),
            Claims = JsonSerializer.SerializeToElement(new Dictionary<string, object>
            {
                ["sub"] = "operator", ["typ"] = "Bearer", ["sid"] = Sid, ["scope"] = "openid organization:firm-a domain-claims",
                ["organization"] = new Dictionary<string, object> { ["firm-a"] = new { } },
                ["realm_access"] = new { roles = new[] { "platform_operator" } },
                ["tenant_id"] = "firm-b", ["role"] = "USER",
            }).EnumerateObject().ToDictionary(property => property.Name, property => (object)property.Value),
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(rsa) { KeyId = "fixture" }, SecurityAlgorithms.RsaSha256),
        }));
        await using var mcp = await Connect(client);

        var result = await mcp.CallToolAsync("search_codebase", new Dictionary<string, object?> { ["query"] = "tenant" }, cancellationToken: Ct);

        Assert.False(result.IsError == true);
        Assert.Equal(Operator, Assert.Single(ranker.Actors));
    }

    [Theory]
    [InlineData("other-session", "firm-a")]
    [InlineData(Sid, "firm-b")]
    public async Task The_direct_MCP_boundary_cannot_borrow_another_session_or_tenants_grant(string sid, string tenant)
    {
        var store = new PermissionStore();
        using var api = Api(store);
        await api.Services.GetRequiredService<ContentAccessGrants>().IssueAsync(Operator, Key(), "INC-123", 10, Ct);
        var ranker = new RecordingRanker();
        var (server, http) = Resource("code", store, ranker);
        using var lifetime = server;
        using var client = http;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(Operator with { TenantId = TenantId.Firm(tenant) }, "code", sid));
        await using var mcp = await Connect(client);

        var error = await Assert.ThrowsAsync<McpProtocolException>(async () => await mcp.CallToolAsync("search_codebase",
            new Dictionary<string, object?> { ["query"] = "tenant" }, cancellationToken: Ct));

        Assert.Contains("active break-glass grant", error.Message);
        Assert.Empty(ranker.Actors);
    }

    [Fact]
    public async Task A_shared_store_outage_fails_closed_at_the_direct_MCP_boundary_before_corpus_read()
    {
        var store = new PermissionStore();
        using var api = Api(store);
        await api.Services.GetRequiredService<ContentAccessGrants>().IssueAsync(Operator, Key(), "INC-123", 10, Ct);
        store.FailRead = true;
        var ranker = new RecordingRanker();
        var (server, http) = Resource("code", store, ranker);
        using var lifetime = server;
        using var client = http;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(Operator, "code"));
        await using var mcp = await Connect(client);

        var error = await Assert.ThrowsAsync<McpProtocolException>(async () => await mcp.CallToolAsync("search_codebase",
            new Dictionary<string, object?> { ["query"] = "tenant" }, cancellationToken: Ct));

        Assert.Contains("active break-glass grant", error.Message);
        Assert.Empty(ranker.Actors);
    }

    [Theory]
    [InlineData(Role.USER)]
    [InlineData(Role.TENANT_ADMIN)]
    public async Task Normal_tenant_roles_can_still_call_the_actual_MCP_host_without_break_glass(Role role)
    {
        var ranker = new RecordingRanker();
        var actor = Operator with { Role = role };
        var (server, http) = Resource("code", new PermissionStore(), ranker);
        using var lifetime = server;
        using var client = http;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(actor, "code"));
        await using var mcp = await Connect(client);

        var result = await mcp.CallToolAsync("search_codebase", new Dictionary<string, object?> { ["query"] = "tenant" }, cancellationToken: Ct);

        Assert.False(result.IsError == true);
        Assert.Equal(actor, Assert.Single(ranker.Actors));
    }

    private static ApiFactory Api(PermissionStore store, TimeProvider? time = null, bool monitor = false, RecordingTraceStore? traces = null) => new(ApiFactory.ProceduralModel())
    {
        InstalledPlugins = monitor ? [Plugins.FixturePlugin.Manifest(), MonitorManifest] : [Plugins.FixturePlugin.Manifest()], BootstrapTenantPlugins = false,
        ConfigureTestServices = services =>
        {
            services.RemoveAll<IBreakGlassPermissionStore>();
            services.AddSingleton<IBreakGlassPermissionStore>(store);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton(time ?? TimeProvider.System);
            if (traces is not null)
            {
                services.RemoveAll<IRunTraceStore>();
                services.AddSingleton<IRunTraceStore>(traces);
            }
        },
    };

    private static PluginManifest MonitorManifest => new()
    {
        Name = "monitor", Kind = PluginKinds.App, Scope = PluginScopes.Installation, Environments = ["dev", "qa"],
        Description = "Real monitor boundary fixture", Progress = "None", Stopping = "None",
    };

    private static HttpClient Client(ApiFactory api, Principal principal, string sid = Sid)
    {
        var client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(principal, "api", sid));
        return client;
    }

    private static string Token(Principal principal, string audience, string sid = Sid) =>
        DevJwt.Issue(new AuthOptions(), principal.UserId, principal.TenantId, principal.Role, audience: audience, sessionId: sid).Token;

    private static async Task SeedConversation(ApiFactory api, Principal owner)
    {
        await using var db = await api.Services.GetRequiredService<IDbContextFactory<MafDbContext>>().CreateDbContextAsync(Ct);
        db.Conversations.Add(new ConversationRow { Id = Conversation, UserId = owner.UserId, TenantId = owner.TenantId.Value,
            CreatedAt = DateTime.UtcNow, LastActivityAt = DateTime.UtcNow });
        db.Turns.Add(new TurnRow { Id = "private-turn", ConversationId = Conversation, UserId = owner.UserId, TenantId = owner.TenantId.Value,
            Question = "private-question", Answer = "private-answer", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync(Ct);
    }

    private static async Task InvalidateAsync(string fault, PermissionStore store, FakeTimeProvider clock, ContentAccessGrants grants, ContentGrantDto grant)
    {
        switch (fault)
        {
            case "expiry": clock.Advance(TimeSpan.FromMinutes(2)); break;
            case "revoked": await store.RevokeAsync(Key(), grant.Id.ToString(CultureInfo.InvariantCulture), grant.ExpiresAt, Ct); break;
            case "ending": store.FailRevoke = true; await grants.EndAsync(Operator, Key(), grant.Id, Ct); break;
            case "outage": store.FailRead = true; break;
            default: throw new ArgumentOutOfRangeException(nameof(fault));
        }
    }

    private static Task<McpClient> Connect(HttpClient http) => McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
    {
        Endpoint = new Uri(http.BaseAddress!, "/mcp"), TransportMode = HttpTransportMode.StreamableHttp,
    }, http, NullLoggerFactory.Instance, ownsHttpClient: false), cancellationToken: Ct);

    private static (IDisposable Server, HttpClient Client) Resource(string audience, PermissionStore store, RecordingRanker ranker) => audience switch
    {
        "billing" => Resource<Maf.Lab.Retrieval.Program>(store, ranker),
        "portfolio" => Resource<Maf.Lab.Portfolio.Program>(store, ranker),
        _ => Resource<service::Maf.Lab.CodeSearch.Program>(store, ranker),
    };

    private static (IDisposable Server, HttpClient Client) Resource<T>(PermissionStore store, RecordingRanker ranker, RSA? rsa = null) where T : class
    {
        var server = new WebApplicationFactory<T>().WithWebHostBuilder(builder =>
        {
            builder.UseFixtureEngine();
            builder.WithFakeSharedState();
            builder.WithoutCollectionBootstrap();
            builder.UseSetting("MAF_ENV", rsa is null ? "dev" : "stage");
            builder.UseSetting("Auth:Authority", rsa is null ? "" : CompanyIssuer);
            builder.UseSetting("Auth:ResourceUri", "https://code.fixture.invalid/mcp");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IBreakGlassPermissionStore>();
                services.AddSingleton<IBreakGlassPermissionStore>(store);
                services.RemoveAll<service::Maf.Lab.CodeSearch.Tools.ICodeRanker>();
                services.AddSingleton<service::Maf.Lab.CodeSearch.Tools.ICodeRanker>(ranker);
                if (rsa is not null)
                    services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options => options.Backchannel = new HttpClient(new CompanyMetadata(rsa)));
            });
        });
        return (server, server.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://code.fixture.invalid") }));
    }

    private sealed class RecordingRanker : service::Maf.Lab.CodeSearch.Tools.ICodeRanker
    {
        public List<Principal> Actors { get; } = [];
        public Task<IReadOnlyList<ScoredChunk>> RankAsync(Principal principal, string query, IReadOnlyList<string>? sourceTypes, int k, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Actors.Add(principal);
            return Task.FromResult<IReadOnlyList<ScoredChunk>>([]);
        }
    }

    private sealed class RecordingTraceStore : IRunTraceStore
    {
        public int Reads { get; private set; }
        public Task AppendAsync(string runId, Maf.Lab.Domain.Tracing.TraceEvent traceEvent, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<Maf.Lab.Domain.Tracing.TraceEvent>> ReadAsync(string runId, int afterSeq, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Reads++;
            return Task.FromResult<IReadOnlyList<Maf.Lab.Domain.Tracing.TraceEvent>>([]);
        }
    }

    private sealed class PermissionStore(TimeProvider? time = null) : IBreakGlassPermissionStore
    {
        private readonly FakeBreakGlassPermissionStore _inner = new(time);
        public bool FailRead { get; set; }
        public bool FailRevoke { get; set; }
        public Task<bool> ActivateAsync(ContentPermission permission, CancellationToken ct) => _inner.ActivateAsync(permission, ct);
        public Task RevokeAsync(string sessionKey, string grantId, DateTimeOffset expiresAt, CancellationToken ct) =>
            FailRevoke ? throw new InvalidOperationException("fixture store outage") : _inner.RevokeAsync(sessionKey, grantId, expiresAt, ct);
        public Task<ContentPermission?> ReadAsync(string sessionKey, CancellationToken ct) =>
            FailRead ? throw new InvalidOperationException("fixture store outage") : _inner.ReadAsync(sessionKey, ct);
    }

    private sealed class CompanyMetadata(RSA rsa) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var parameters = rsa.ExportParameters(false);
            object body = request.RequestUri!.AbsoluteUri == CompanyIssuer + "/.well-known/openid-configuration"
                ? new { issuer = CompanyIssuer, jwks_uri = CompanyIssuer + "/jwks" }
                : new { keys = new[] { new { kty = "RSA", use = "sig", kid = "fixture", alg = "RS256",
                    n = Base64UrlEncoder.Encode(parameters.Modulus!), e = Base64UrlEncoder.Encode(parameters.Exponent!) } } };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body) });
        }
    }
}
