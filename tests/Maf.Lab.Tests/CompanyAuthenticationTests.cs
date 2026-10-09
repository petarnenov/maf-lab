extern alias service;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Maf.Lab.Domain.Configuration;
using Maf.Lab.Retrieval.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Maf.Lab.TestSupport;
using Maf.Lab.Plugins.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;

namespace Maf.Lab.Tests;

public sealed class CompanyAuthenticationTests
{
    private const string Issuer = "https://company.identity.invalid/realms/lab";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("api")]
    [InlineData("billing")]
    [InlineData("portfolio")]
    [InlineData("code")]
    public async Task Actual_JWKS_bearer_handler_binds_organization_roles_groups_and_resource_audience(string audience)
    {
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "fixture" };
        var metadata = new Metadata(rsa);
        await using var app = await HostAsync(audience, metadata);
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(key, audience));
        var response = await client.GetAsync("/who", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("firm-a", body.GetProperty("tenant").GetString());
        Assert.Equal("USER", body.GetProperty("role").GetString());
        Assert.Equal(["group-a-id"], body.GetProperty("groups").EnumerateArray().Select(g => g.GetString()));
        Assert.Contains("/.well-known/openid-configuration", metadata.Paths.Select(path => path[Issuer.Length..]));
        Assert.Contains("/jwks", metadata.Paths.Select(path => path[Issuer.Length..]));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(key, "another-resource"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/who", Ct)).StatusCode);
    }

    [Theory]
    [InlineData("issuer")]
    [InlineData("signature")]
    [InlineData("expired")]
    [InlineData("id-token")]
    [InlineData("many-organizations")]
    [InlineData("missing-organization")]
    [InlineData("string-organization")]
    [InlineData("string-realm-roles")]
    [InlineData("string-client-roles")]
    [InlineData("many-roles")]
    [InlineData("malformed-groups")]
    [InlineData("top-groups-object")]
    [InlineData("top-groups-number")]
    [InlineData("top-groups-boolean")]
    [InlineData("delegation")]
    public async Task Wrong_or_ambiguous_company_credentials_are_rejected_by_the_actual_HTTP_pipeline(string fault)
    {
        using var rsa = RSA.Create(2048);
        using var rogue = RSA.Create(2048);
        var key = new RsaSecurityKey(fault == "signature" ? rogue : rsa) { KeyId = "fixture" };
        await using var app = await HostAsync("api", new Metadata(rsa));
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(key, "api", fault));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/who", Ct)).StatusCode);
    }

    [Theory]
    [InlineData("stage")]
    [InlineData("prod")]
    public void Stage_and_prod_never_fall_back_to_the_symmetric_dev_issuer(string environment)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["MAF_ENV"] = environment }).Build();
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddLabAuthentication(configuration));
        configuration["Auth:Authority"] = "http://localhost:8080/realms/lab";
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddLabAuthentication(configuration));
    }

    [Fact]
    public async Task Company_tokens_authorize_real_API_permission_routes_for_the_token_organization_and_core_role()
    {
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "fixture" };
        using var api = new ApiFactory(ApiFactory.ProceduralModel())
        {
            BootstrapTenantPlugins = false,
            ExtraSettings = new Dictionary<string, string?> { ["Auth:Authority"] = Issuer },
            ConfigureTestServices = services => services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme,
                options => options.Backchannel = new HttpClient(new Metadata(rsa))),
        };
        using var client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(key, "api"));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/platform/plugins", Ct)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(key, "api", role: "platform_operator"));
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/platform/plugins/{StandInDomains.PortfolioManifest.Name}", new { allowed = true, enabled = true }, Ct)).StatusCode);
        var permissions = await api.Services.GetRequiredService<IPluginEntitlements>()
            .ReadAsync(new Maf.Lab.Domain.Tenancy.Principal("alice", Maf.Lab.Domain.Tenancy.TenantId.Firm("firm-a"), Maf.Lab.Domain.Tenancy.Role.PLATFORM_ADMIN), Ct);
        Assert.Equal(StandInDomains.PortfolioManifest.Name, Assert.Single(permissions).Plugin);
        Assert.Empty(await api.Services.GetRequiredService<IPluginEntitlements>()
            .ReadAsync(new Maf.Lab.Domain.Tenancy.Principal("alice", Maf.Lab.Domain.Tenancy.TenantId.Firm("firm-b"), Maf.Lab.Domain.Tenancy.Role.PLATFORM_ADMIN), Ct));
    }

    [Theory]
    [InlineData("billing")]
    [InlineData("portfolio")]
    [InlineData("code")]
    public async Task Company_tokens_reach_each_real_resource_host_official_MCP_transport(string audience)
    {
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "fixture" };
        var (server, client) = audience switch
        {
            "billing" => Resource<Maf.Lab.Retrieval.Program>(rsa, "billing"),
            "portfolio" => Resource<Maf.Lab.Portfolio.Program>(rsa, "portfolio"),
            _ => Resource<service::Maf.Lab.CodeSearch.Program>(rsa, "code"),
        };
        using var lifetime = server;
        using var http = client;
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(key, "api"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsync("/mcp", null, Ct)).StatusCode);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(key, audience));
        await using var mcp = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(http.BaseAddress!, "/mcp"), TransportMode = HttpTransportMode.StreamableHttp,
        }, http, NullLoggerFactory.Instance, ownsHttpClient: false), cancellationToken: Ct);
        Assert.NotEmpty(await mcp.ListToolsAsync(cancellationToken: Ct));
    }

    private static (IDisposable Server, HttpClient Client) Resource<T>(RSA rsa, string audience) where T : class
    {
        var server = new WebApplicationFactory<T>().WithWebHostBuilder(builder =>
        {
            builder.UseFixtureEngine(); builder.WithFakeSharedState(); builder.WithoutCollectionBootstrap();
            builder.UseSetting("Auth:Authority", Issuer); builder.UseSetting("Auth:Audience", "api"); builder.UseSetting("MAF_ENV", "stage");
            builder.UseSetting("Auth:ResourceUri", $"https://{audience}.fixture.invalid/{(audience == "billing" ? "" : audience + "/")}mcp");
            builder.ConfigureTestServices(services => services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme,
                options => options.Backchannel = new HttpClient(new Metadata(rsa))));
        });
        return (server, server.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri($"https://{audience}.fixture.invalid") }));
    }

    [Fact]
    public async Task A_development_token_never_becomes_valid_under_a_company_authority()
    {
        using var rsa = RSA.Create(2048);
        await using var app = await HostAsync("api", new Metadata(rsa));
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", DevJwt.Issue(new AuthOptions(), "alice", Maf.Lab.Domain.Tenancy.TenantId.Firm("firm-a"), Maf.Lab.Domain.Tenancy.Role.USER).Token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/who", Ct)).StatusCode);
    }

    [Theory]
    [InlineData("api")]
    [InlineData("billing")]
    [InlineData("portfolio")]
    [InlineData("code")]
    public async Task Company_operator_scopes_are_enforced_by_every_shared_JWKS_resource_boundary(string audience)
    {
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "fixture" };
        await using var app = await HostAsync(audience, new Metadata(rsa));
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(key, audience,
            role: "platform_operator", scope: "openid organization:firm-a domain-claims"));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/who", Ct)).StatusCode);
        foreach (var scope in new[] { "organization", "organization:*", "organization:firm-b", "organization:firm-a organization:firm-b" })
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(key, audience,
                role: "platform_operator", scope: scope));
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/who", Ct)).StatusCode);
        }
    }

    private static async Task<WebApplication> HostAsync(string audience, Metadata metadata)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["MAF_ENV"] = "stage", ["Auth:Authority"] = Issuer });
        builder.Services.AddLabAuthentication(builder.Configuration, audience);
        builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options => options.Backchannel = new HttpClient(metadata));
        var app = builder.Build();
        app.UseAuthentication(); app.UseAuthorization();
        app.MapGet("/who", (IPrincipalAccessor principal) => Microsoft.AspNetCore.Http.Results.Ok(new
        {
            tenant = principal.Current.TenantId.Value, role = principal.Current.Role.ToString(), groups = principal.Current.GroupIds,
        })).RequireAuthorization();
        await app.StartAsync(Ct);
        return app;
    }

    private static string Token(SecurityKey key, string audience, string? fault = null, string? role = null, string? scope = null)
    {
        var organization = new Dictionary<string, object> { ["firm-a"] = new { groups = fault == "malformed-groups" ? (object)"admin" : new[] { "/Finance" } } };
        if (fault == "many-organizations") organization["firm-b"] = new { };
        var claims = new Dictionary<string, object>
        {
            ["sub"] = "alice", ["typ"] = fault == "id-token" ? "ID" : "Bearer", ["organization"] = organization,
            ["groups"] = new[] { "group-a-id" },
            ["realm_access"] = new { roles = fault == "many-roles" ? new[] { "USER", "TENANT_ADMIN" } : new[] { role ?? "USER", "offline_access" } },
            ["tenant_id"] = "firm-b", ["role"] = "PLATFORM_ADMIN",
        };
        if (role == "platform_operator")
        {
            claims["sid"] = "company-operator-session";
            claims["scope"] = scope ?? "openid organization:firm-a";
        }
        if (fault == "missing-organization") claims.Remove("organization");
        if (fault == "string-organization") claims["organization"] = JsonSerializer.Serialize(organization);
        if (fault == "string-realm-roles") claims["realm_access"] = "{\"roles\":[\"USER\"]}";
        if (fault == "string-client-roles") claims["resource_access"] = "{\"api\":{\"roles\":[\"USER\"]}}";
        if (fault == "top-groups-object") claims["groups"] = new { admin = true };
        if (fault == "top-groups-number") claims["groups"] = new[] { 1 };
        if (fault == "top-groups-boolean") claims["groups"] = true;
        if (fault == "delegation") claims["act"] = new { sub = "other" };
        var now = DateTime.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = fault == "issuer" ? "https://other.identity.invalid/realms/lab" : Issuer,
            Audience = audience, Claims = JsonSerializer.SerializeToElement(claims).EnumerateObject()
                .ToDictionary(property => property.Name, property => (object)property.Value),
            IssuedAt = now.AddMinutes(-10), NotBefore = now.AddMinutes(-10),
            Expires = fault == "expired" ? now.AddMinutes(-2) : now.AddMinutes(5),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256),
        });
    }

    private sealed class Metadata(RSA rsa) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Paths.Add(request.RequestUri!.AbsoluteUri);
            Assert.Null(request.Headers.Authorization);
            var parameters = rsa.ExportParameters(false);
            object result = request.RequestUri.AbsoluteUri == Issuer + "/.well-known/openid-configuration"
                ? new { issuer = Issuer, jwks_uri = Issuer + "/jwks" }
                : new { keys = new[] { new { kty = "RSA", use = "sig", kid = "fixture", alg = "RS256", n = Base64UrlEncoder.Encode(parameters.Modulus!), e = Base64UrlEncoder.Encode(parameters.Exponent!) } } };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(result) });
        }
    }
}
