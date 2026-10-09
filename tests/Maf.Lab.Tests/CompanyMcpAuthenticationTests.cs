extern alias service;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Maf.Lab.Retrieval.Auth;
using Maf.Lab.TestSupport;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Maf.Lab.Tests;

public sealed class CompanyMcpAuthenticationTests
{
    private const string Authority = "https://company.identity.invalid/realms/lab";
    private const string BillingResource = "https://billing.fixture.invalid/mcp";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("billing", "https://billing.fixture.invalid/mcp")]
    [InlineData("portfolio", "https://portfolio.fixture.invalid/portfolio/mcp")]
    [InlineData("code", "https://code.fixture.invalid/code/mcp")]
    public async Task Each_company_resource_host_publishes_anonymous_pinned_metadata_without_contacting_the_IdP(string host, string resource)
    {
        using var rsa = RSA.Create(2048);
        var backchannel = new IdentityMetadata(rsa);
        var (server, client) = Resource(host, resource, backchannel);
        using var lifetime = server;
        using var http = client;

        using var response = await http.GetAsync(MetadataPath(resource), Ct);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(resource, body.GetProperty("resource").GetString());
        Assert.Equal([Authority], body.GetProperty("authorization_servers").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(["header"], body.GetProperty("bearer_methods_supported").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(["authorization_servers", "bearer_methods_supported", "resource", "scopes_supported"], body.EnumerateObject().Select(property => property.Name).Order());
        Assert.Empty(body.GetProperty("scopes_supported").EnumerateArray());
        Assert.Empty(backchannel.Paths);
    }

    [Theory]
    [InlineData("billing", "https://billing.fixture.invalid/mcp", false)]
    [InlineData("portfolio", "https://portfolio.fixture.invalid/portfolio/mcp", false)]
    [InlineData("code", "https://code.fixture.invalid/code/mcp", false)]
    [InlineData("billing", "https://billing.fixture.invalid/mcp", true)]
    [InlineData("portfolio", "https://portfolio.fixture.invalid/portfolio/mcp", true)]
    [InlineData("code", "https://code.fixture.invalid/code/mcp", true)]
    public async Task Real_MCP_challenges_advertise_the_configured_resource_for_missing_or_wrong_audience_tokens(string host, string resource, bool wrongAudience)
    {
        using var rsa = RSA.Create(2048);
        var backchannel = new IdentityMetadata(rsa);
        var (server, client) = Resource(host, resource, backchannel);
        using var lifetime = server;
        using var http = client;
        http.DefaultRequestHeaders.Authorization = wrongAudience ? new AuthenticationHeaderValue("Bearer", Token(rsa, "api")) : null;

        using var response = await http.PostAsync("/mcp", null, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var challenge = Assert.Single(response.Headers.WwwAuthenticate);
        Assert.Equal("Bearer", challenge.Scheme);
        Assert.Contains($"resource_metadata=\"{MetadataUri(resource)}\"", challenge.Parameter);
    }

    [Theory]
    [InlineData("http://billing.fixture.invalid")]
    [InlineData("https://attacker.invalid")]
    public async Task Metadata_is_not_served_for_a_noncanonical_request_hostname_or_scheme(string requestOrigin)
    {
        using var rsa = RSA.Create(2048);
        var backchannel = new IdentityMetadata(rsa);
        var (server, client) = Resource("billing", BillingResource, backchannel, requestOrigin);
        using var lifetime = server;
        using var http = client;

        using var response = await http.GetAsync(MetadataPath(BillingResource), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(backchannel.Paths);
    }

    [Fact]
    public async Task Native_SDK_port_matching_does_not_change_the_advertised_canonical_resource_or_challenge()
    {
        using var rsa = RSA.Create(2048);
        const string resource = "https://billing.fixture.invalid:8443/mcp";
        var backchannel = new IdentityMetadata(rsa);
        var (server, client) = Resource("billing", resource, backchannel, "https://billing.fixture.invalid:9443");
        using var lifetime = server;
        using var http = client;
        using var metadata = await http.GetAsync(MetadataPath(resource), Ct);
        Assert.Equal(HttpStatusCode.OK, metadata.StatusCode);
        var body = await metadata.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(resource, body.GetProperty("resource").GetString());
        using var challenge = await http.PostAsync("/mcp", null, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, challenge.StatusCode);
        Assert.Contains($"resource_metadata=\"{MetadataUri(resource)}\"", Assert.Single(challenge.Headers.WwwAuthenticate).Parameter);
        Assert.Empty(backchannel.Paths);
    }

    [Theory]
    [InlineData("billing", "https://billing.fixture.invalid/mcp", "10.80.0.2")]
    [InlineData("portfolio", "https://portfolio.fixture.invalid/portfolio/mcp", "10.80.0.2")]
    [InlineData("code", "https://code.fixture.invalid/code/mcp", "10.80.0.2")]
    [InlineData("billing", "https://billing.fixture.invalid/mcp", "::ffff:10.80.0.2")]
    public async Task Trusted_explicit_proxy_network_can_restore_the_canonical_HTTPS_origin(string host, string resource, string remoteAddress)
    {
        using var rsa = RSA.Create(2048);
        var backchannel = new IdentityMetadata(rsa);
        var (server, client) = Resource(host, resource, backchannel, "http://backend.internal", remoteAddress, "10.80.0.2/32");
        using var lifetime = server;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Forwarded-Host", new Uri(resource).Host);
        http.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");

        using var response = await http.GetAsync(MetadataPath(resource), Ct);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(resource, body.GetProperty("resource").GetString());
        Assert.Empty(backchannel.Paths);
    }

    [Theory]
    [InlineData("10.81.0.2", "billing.fixture.invalid", "https")]
    [InlineData("127.0.0.1", "billing.fixture.invalid", "https")]
    [InlineData("none", "billing.fixture.invalid", "https")]
    [InlineData("10.80.0.2", "attacker.invalid", "https")]
    [InlineData("10.80.0.2", "billing.fixture.invalid", "")]
    [InlineData("10.80.0.2", "", "https")]
    [InlineData("10.80.0.2", "billing.fixture.invalid, backend.internal", "https, http")]
    public async Task Untrusted_spoofed_or_asymmetric_forwarding_cannot_publish_metadata(string remoteAddress, string forwardedHost, string forwardedProto)
    {
        using var rsa = RSA.Create(2048);
        var backchannel = new IdentityMetadata(rsa);
        var (server, client) = Resource("billing", BillingResource, backchannel, "http://backend.internal", remoteAddress, "10.80.0.2/32");
        using var lifetime = server;
        using var http = client;
        http.DefaultRequestHeaders.TryAddWithoutValidation("X-Forwarded-Host", forwardedHost);
        http.DefaultRequestHeaders.TryAddWithoutValidation("X-Forwarded-Proto", forwardedProto);

        using var response = await http.GetAsync(MetadataPath(BillingResource), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(backchannel.Paths);
    }

    [Fact]
    public async Task Forwarded_headers_never_override_the_resource_in_the_MCP_challenge()
    {
        using var rsa = RSA.Create(2048);
        var (server, client) = Resource("billing", BillingResource, new IdentityMetadata(rsa), "http://attacker.invalid", "10.80.0.2", "10.80.0.2/32");
        using var lifetime = server;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Forwarded-Host", "attacker.invalid");
        http.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");

        using var response = await http.PostAsync("/mcp", null, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains($"resource_metadata=\"{MetadataUri(BillingResource)}\"", Assert.Single(response.Headers.WwwAuthenticate).Parameter);
    }

    [Fact]
    public async Task Empty_proxy_configuration_does_not_trust_the_framework_default_loopback_proxy()
    {
        using var rsa = RSA.Create(2048);
        var (server, client) = Resource("billing", BillingResource, new IdentityMetadata(rsa), "http://backend.internal", "127.0.0.1", "");
        using var lifetime = server;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Forwarded-Host", "billing.fixture.invalid");
        http.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");

        using var response = await http.GetAsync(MetadataPath(BillingResource), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("/mcp")]
    [InlineData("http://billing.fixture.invalid/mcp")]
    [InlineData("https://user:password@billing.fixture.invalid/mcp")]
    [InlineData("https://billing.fixture.invalid/mcp?tenant=firm-a")]
    [InlineData("https://billing.fixture.invalid/mcp#callback")]
    [InlineData("https://billing.fixture.invalid/api")]
    public void Company_MCP_startup_refuses_missing_or_unsafe_public_resource_URIs(string? resource)
    {
        var configuration = Configuration(resource);
        var services = new ServiceCollection();
        services.AddLabAuthentication(configuration, "billing");

        Assert.Throws<InvalidOperationException>(() => services.AddLabMcpAuthentication(configuration));
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("10.80.0.2")]
    [InlineData("10.80.0.2/33")]
    [InlineData("0.0.0.0/0")]
    [InlineData("::/0")]
    public void Company_MCP_startup_refuses_invalid_or_unrestricted_proxy_networks(string proxyNetwork)
    {
        var configuration = Configuration(BillingResource, proxyNetwork);
        var services = new ServiceCollection();
        services.AddLabAuthentication(configuration, "billing");

        Assert.Throws<InvalidOperationException>(() => services.AddLabMcpAuthentication(configuration));
    }

    [Theory]
    [InlineData("dev", "http://localhost:8080/mcp")]
    [InlineData("qa", "http://127.0.0.1:8080/mcp")]
    [InlineData("prod", "https://billing.fixture.invalid/mcp")]
    public void Company_MCP_accepts_HTTPS_or_explicit_local_fixture_resources(string environment, string resource)
    {
        var configuration = Configuration(resource);
        configuration["MAF_ENV"] = environment;
        var services = new ServiceCollection();
        services.AddLabAuthentication(configuration, "billing");

        services.AddLabMcpAuthentication(configuration);
    }

    [Fact]
    public async Task Development_without_company_authority_retains_existing_MCP_authentication_and_exposes_no_company_metadata()
    {
        var (server, client) = Resource("billing", BillingResource, null, authority: null);
        using var lifetime = server;
        using var http = client;

        using var metadata = await http.GetAsync(MetadataPath(BillingResource), Ct);
        using var unauthorized = await http.PostAsync("/mcp", null, Ct);

        Assert.Equal(HttpStatusCode.NotFound, metadata.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.DoesNotContain(unauthorized.Headers.WwwAuthenticate, challenge => challenge.Parameter?.Contains("resource_metadata", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task Company_API_retains_its_ordinary_bearer_challenge_without_MCP_resource_metadata()
    {
        using var rsa = RSA.Create(2048);
        var backchannel = new IdentityMetadata(rsa);
        using var api = new ApiFactory(ApiFactory.ProceduralModel())
        {
            BootstrapTenantPlugins = false,
            ExtraSettings = new Dictionary<string, string?> { ["Auth:Authority"] = Authority },
            ConfigureTestServices = services => services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme,
                options => options.Backchannel = new HttpClient(backchannel)),
        };
        using var http = api.CreateClient();

        using var metadata = await http.GetAsync(MetadataPath(BillingResource), Ct);
        using var unauthorized = await http.GetAsync("/api/platform/plugins", Ct);

        Assert.Equal(HttpStatusCode.NotFound, metadata.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.DoesNotContain(unauthorized.Headers.WwwAuthenticate, challenge => challenge.Parameter?.Contains("resource_metadata", StringComparison.Ordinal) == true);
        Assert.Empty(backchannel.Paths);
    }

    private static IConfigurationRoot Configuration(string? resource, string? proxyNetwork = null) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MAF_ENV"] = "stage", ["Auth:Authority"] = Authority, ["Auth:ResourceUri"] = resource,
            ["Auth:TrustedProxyNetworks:0"] = proxyNetwork,
        }).Build();

    private static (IDisposable Server, HttpClient Client) Resource(string host, string resource, IdentityMetadata? metadata,
        string? requestOrigin = null, string? remoteAddress = null, string? proxyNetwork = null, string? authority = Authority) => host switch
    {
        "billing" => Resource<Maf.Lab.Retrieval.Program>(resource, metadata, requestOrigin, remoteAddress, proxyNetwork, authority),
        "portfolio" => Resource<Maf.Lab.Portfolio.Program>(resource, metadata, requestOrigin, remoteAddress, proxyNetwork, authority),
        _ => Resource<service::Maf.Lab.CodeSearch.Program>(resource, metadata, requestOrigin, remoteAddress, proxyNetwork, authority),
    };

    private static (IDisposable Server, HttpClient Client) Resource<T>(string resource, IdentityMetadata? metadata,
        string? requestOrigin, string? remoteAddress, string? proxyNetwork, string? authority) where T : class
    {
        var server = new WebApplicationFactory<T>().WithWebHostBuilder(builder =>
        {
            builder.UseFixtureEngine();
            builder.WithFakeSharedState();
            builder.WithoutCollectionBootstrap();
            builder.UseSetting("MAF_ENV", authority is null ? "dev" : "stage");
            builder.UseSetting("Auth:Authority", authority ?? "");
            builder.UseSetting("Auth:ResourceUri", resource);
            builder.UseSetting("Auth:TrustedProxyNetworks:0", proxyNetwork ?? "");
            builder.ConfigureTestServices(services =>
            {
                services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options => options.Backchannel = new HttpClient((HttpMessageHandler?)metadata ?? new RejectBackchannel()));
                services.AddSingleton<IStartupFilter>(new RemoteAddress(remoteAddress == "none" ? null : IPAddress.Parse(remoteAddress ?? "192.0.2.1")));
            });
        });
        var origin = requestOrigin ?? new Uri(resource).GetLeftPart(UriPartial.Authority);
        return (server, server.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri(origin), AllowAutoRedirect = false }));
    }

    private static string MetadataPath(string resource) => "/.well-known/oauth-protected-resource" + new Uri(resource).AbsolutePath;
    private static string MetadataUri(string resource) => new Uri(new Uri(resource), MetadataPath(resource)).AbsoluteUri;

    private static string Token(RSA rsa, string audience) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Issuer = Authority, Audience = audience, IssuedAt = DateTime.UtcNow.AddMinutes(-1), NotBefore = DateTime.UtcNow.AddMinutes(-1), Expires = DateTime.UtcNow.AddMinutes(5),
        Claims = JsonSerializer.SerializeToElement(new Dictionary<string, object>
        {
            ["sub"] = "alice", ["typ"] = "Bearer", ["organization"] = new Dictionary<string, object> { ["firm-a"] = new { } },
            ["realm_access"] = new { roles = new[] { "USER" } },
        }).EnumerateObject().ToDictionary(property => property.Name, property => (object)property.Value),
        SigningCredentials = new SigningCredentials(new RsaSecurityKey(rsa) { KeyId = "fixture" }, SecurityAlgorithms.RsaSha256),
    });

    private sealed class RemoteAddress(IPAddress? address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, continuation) =>
            {
                context.Connection.RemoteIpAddress = address;
                return continuation(context);
            });
            next(app);
        };
    }

    private sealed class RejectBackchannel : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => throw new InvalidOperationException("Anonymous resource metadata must not request IdP discovery.");
    }

    private sealed class IdentityMetadata(RSA rsa) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsoluteUri);
            var key = rsa.ExportParameters(false);
            object response = request.RequestUri.AbsoluteUri == Authority + "/.well-known/openid-configuration"
                ? new { issuer = Authority, jwks_uri = Authority + "/jwks" }
                : new { keys = new[] { new { kty = "RSA", use = "sig", kid = "fixture", alg = "RS256", n = Base64UrlEncoder.Encode(key.Modulus!), e = Base64UrlEncoder.Encode(key.Exponent!) } } };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(response) });
        }
    }
}
