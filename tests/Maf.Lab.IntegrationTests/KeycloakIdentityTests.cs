using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Retrieval.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Maf.Lab.Api.Plugins;
using Maf.Lab.Domain.Configuration;
using Maf.Lab.Plugins.Abstractions;
using Maf.Lab.TestSupport;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.Testing;
using ModelContextProtocol.Client;

namespace Maf.Lab.IntegrationTests;

/// <summary>Real pinned external IdP behavior, isolated from compose, models, corpus and graph work.</summary>
public sealed class KeycloakIdentityFixture : IAsyncLifetime
{
    public const string Image = "quay.io/keycloak/keycloak:26.8.0";
    private readonly IContainer _container = new ContainerBuilder(Image)
        .WithPortBinding(8080, true)
        .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", "fixture-admin")
        .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", "fixture-admin-password")
        .WithEnvironment("KC_FEATURES_DISABLED", "token-exchange,token-exchange-delegation")
        .WithResourceMapping(new FileInfo(Path.Combine(AppContext.BaseDirectory, "Identity/fixture.realm.json")), "/opt/keycloak/data/import/")
        .WithCommand("start-dev", "--import-realm", "--hostname-strict=false")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(8080).ForPath("/realms/maf-fixture/.well-known/openid-configuration")))
        .Build();
    public string Authority => $"http://{_container.Hostname}:{_container.GetMappedPublicPort(8080)}/realms/maf-fixture";
    public async ValueTask InitializeAsync() => await _container.StartAsync();
    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}

[CollectionDefinition(Name)]
public sealed class KeycloakIdentityCollection : ICollectionFixture<KeycloakIdentityFixture>
{
    public const string Name = "keycloak-identity";
}

[Collection(KeycloakIdentityCollection.Name)]
public sealed class KeycloakIdentityTests(KeycloakIdentityFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string Callback = "http://127.0.0.1:7171/auth/callback";

    [Fact]
    public async Task Public_web_authorization_code_PKCE_token_builds_the_organization_role_and_group_ID_principal()
    {
        var token = await SignInAsync("web", "fixture-a", "firm-a");
        await using var api = await HostAsync("api");
        using var client = api.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.GetAsync("/principal", Ct);
        response.EnsureSuccessStatusCode();
        var principal = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;
        Assert.Equal("firm-a", principal.GetProperty("tenant").GetString());
        Assert.Equal("USER", principal.GetProperty("role").GetString());
        Assert.Equal(new[] { "00000000-0000-4000-8000-000000000001", "00000000-0000-4000-8000-000000000002" },
            principal.GetProperty("groups").EnumerateArray().Select(g => g.GetString()).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Native_signed_token_from_another_configured_issuer_is_refused_even_when_discovery_trusts_its_key()
    {
        var token = await SignInAsync("web", "fixture-a", "firm-a");
        await using var api = await HostAsync("api", fixture.Authority + "-other");
        using var client = api.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/principal", Ct)).StatusCode);
    }

    [Fact]
    public async Task Supported_V2_exchange_retains_identity_domain_claims_and_exact_resource_audience()
    {
        var subject = await SignInAsync("web", "fixture-a", "firm-a");
        var raw = new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(subject);
        Assert.False(string.IsNullOrWhiteSpace(raw.Subject));
        Assert.True(CompanyIdentityClaims.TryNormalize(new ClaimsPrincipal(new ClaimsIdentity(raw.Claims, "fixture")), "api", out var identity));
        Assert.True(PrincipalClaims.TryCreate(identity, out var caller));
        Assert.Equal("firm-a", caller.TenantId.Value);
        await using var api = await HostAsync("api");
        await using var state = new TenantPluginFixture(tenantPlugin: "billing");
        await state.InitializeAsync(Ct);
        var changes = new PluginAccessChanges(NullLogger<PluginAccessChanges>.Instance);
        var permissions = state.Store(changes);
        await permissions.AllowAsync(TenantPluginFixture.Operator, "billing", true, true, Ct);
        var exchange = new PluginTokenExchange(state.Access(permissions, changes), state.Catalogue,
            api.Services.GetRequiredService<IHttpClientFactory>(), Options.Create(new PluginTokenExchangeOptions
            {
                TokenEndpoint = fixture.Authority + "/protocol/openid-connect/token", ClientId = "api", ClientSecret = "fixture-api-client-secret",
            }), api.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>(), TimeProvider.System,
            api.Services.GetRequiredService<IOptions<AuthOptions>>());
        // Production code must select the principal's organization for this A+B member; no test-only scope override.
        var token = await exchange.ForAsync(caller, "billing", subject, Ct);
        var parsed = new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(token);
        Assert.Equal(["billing"], parsed.Audiences);
        Assert.Contains(parsed.Claims, c => c.Type == "domain_roles" && c.Value == "billing:advisor");
        Assert.Contains(parsed.Claims, c => c.Type == "advisor_ids" && c.Value == "adv-a-1");
        await using var resource = await HostAsync("billing");
        using var client = resource.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var principal = JsonDocument.Parse(await client.GetStringAsync("/principal", Ct)).RootElement;
        Assert.Equal("firm-a", principal.GetProperty("tenant").GetString());
        Assert.Equal(new[] { "00000000-0000-4000-8000-000000000001", "00000000-0000-4000-8000-000000000002" },
            principal.GetProperty("groups").EnumerateArray().Select(g => g.GetString()).Order(StringComparer.Ordinal));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", subject);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/principal", Ct)).StatusCode);

        var seedPath = Path.Combine(state.DirectoryPath, "billing-seed.json");
        await File.WriteAllTextAsync(seedPath, """
            [{"firmId":"firm-a","runId":"4417","status":"completed","periodStart":"2026-06-01","periodEnd":"2026-06-30",
              "accountCount":10,"failureReason":null,"updatedAt":"2026-07-01T00:00:00Z"},
             {"firmId":"firm-b","runId":"5517","status":"completed","periodStart":"2026-06-01","periodEnd":"2026-06-30",
              "accountCount":20,"failureReason":null,"updatedAt":"2026-07-01T00:00:00Z"}]
            """, Ct);
        await using var billing = new WebApplicationFactory<Maf.Lab.Retrieval.Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseFixtureEngine();
            builder.WithFakeSharedState();
            builder.WithoutCollectionBootstrap();
            builder.UseSetting("MAF_ENV", "qa");
            builder.UseSetting("Auth:Authority", fixture.Authority);
            builder.UseSetting("Auth:ResourceUri", "http://localhost/mcp");
            builder.UseSetting("Auth:Audience", "api");
            builder.UseSetting("Billing:SeedPath", seedPath);
        });
        using var http = billing.CreateDefaultClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await using var mcp = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(http.BaseAddress!, "/mcp"), TransportMode = HttpTransportMode.StreamableHttp,
        }, http, NullLoggerFactory.Instance, ownsHttpClient: false), cancellationToken: Ct);
        var own = await mcp.CallToolAsync("get_billing_run_status", new Dictionary<string, object?> { ["runId"] = "4417" }, cancellationToken: Ct);
        Assert.NotEqual(true, own.IsError);
        Assert.Equal("4417", own.StructuredContent!.Value.GetProperty("runId").GetString());
        var foreign = await mcp.CallToolAsync("get_billing_run_status", new Dictionary<string, object?> { ["runId"] = "5517" }, cancellationToken: Ct);
        Assert.Equal(true, foreign.IsError);
        using var denied = billing.CreateDefaultClient();
        denied.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", subject);
        Assert.Equal(HttpStatusCode.Unauthorized, (await denied.PostAsync("/mcp", null, Ct)).StatusCode);
    }

    [Fact]
    public async Task Another_requester_and_a_subject_without_api_audience_cannot_use_the_api_exchange()
    {
        using var http = new HttpClient();
        var subject = await SignInAsync("web", "fixture-a", "firm-a");
        var parsed = new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(subject);
        Assert.Contains("api", parsed.Audiences);
        Assert.DoesNotContain("other-requester", parsed.Audiences);
        Assert.Equal("web", parsed.Claims.Single(c => c.Type == "azp").Value);
        using var other = await ExchangeAsync(http, "other-requester", "fixture-other-secret", subject);
        await AssertAudienceRefusalAsync(other);
        var noApi = await SignInAsync("web-without-api", "fixture-a", "firm-a");
        var missingAudience = new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(noApi);
        Assert.DoesNotContain("api", missingAudience.Audiences);
        Assert.Equal("web-without-api", missingAudience.Claims.Single(c => c.Type == "azp").Value);
        using var missing = await ExchangeAsync(http, "api", "fixture-api-client-secret", noApi);
        await AssertAudienceRefusalAsync(missing);
    }

    private async Task<WebApplication> HostAsync(string audience, string? configuredAuthority = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["MAF_ENV"] = "qa", ["Auth:Authority"] = configuredAuthority ?? fixture.Authority });
        builder.Services.AddLabAuthentication(builder.Configuration, audience);
        if (configuredAuthority is not null)
            // Even a discovery document advertising this real issuer/key cannot widen configured issuer trust.
            builder.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme,
                options => options.MetadataAddress = fixture.Authority + "/.well-known/openid-configuration");
        builder.Services.AddHttpClient(PluginTokenExchange.ClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        var app = builder.Build();
        app.UseAuthentication(); app.UseAuthorization();
        app.MapGet("/principal", (IPrincipalAccessor user) => Microsoft.AspNetCore.Http.Results.Ok(new
            { tenant = user.Current.TenantId.Value, role = user.Current.Role.ToString(), groups = user.Current.GroupIds })).RequireAuthorization();
        await app.StartAsync(Ct);
        return app;
    }

    private async Task<string> SignInAsync(string clientId, string username, string organization)
    {
        var verifier = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Guid.NewGuid().ToString("N");
        using var handler = new LoopbackBrowserCookies(new Uri(fixture.Authority));
        using var browser = new HttpClient(handler);
        var parameters = new Dictionary<string, string> { ["client_id"] = clientId, ["response_type"] = "code", ["redirect_uri"] = Callback,
            ["scope"] = "openid profile organization:" + organization + " domain-claims", ["state"] = state,
            ["code_challenge"] = challenge, ["code_challenge_method"] = "S256" };
        var query = string.Join('&', parameters.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)));
        using var login = await browser.GetAsync(fixture.Authority + "/protocol/openid-connect/auth?" + query, Ct);
        login.EnsureSuccessStatusCode();
        var html = await login.Content.ReadAsStringAsync(Ct);
        var form = LoginAction(html);
        using var usernameStep = await browser.PostAsync(form, new FormUrlEncodedContent(new Dictionary<string, string> { ["username"] = username }), Ct);
        var credentials = await usernameStep.Content.ReadAsStringAsync(Ct);
        Assert.True(usernameStep.StatusCode == HttpStatusCode.OK,
            $"Username step returned {usernameStep.StatusCode}; initial fields: " +
            string.Join(",", Regex.Matches(html, "<input[^>]*\\bname=\"([^\"]+)\"", RegexOptions.IgnoreCase).Select(m => m.Groups[1].Value)) +
            "; feedback: " + string.Join(";", Regex.Matches(credentials, "<(?:p|span|h1)[^>]*>([^<]+)</(?:p|span|h1)>", RegexOptions.IgnoreCase).Select(m => m.Groups[1].Value.Trim())));
        Assert.Contains("name=\"password\"", credentials);
        using var sent = await browser.PostAsync(LoginAction(credentials), new FormUrlEncodedContent(new Dictionary<string, string>
            { ["username"] = username, ["password"] = "fixture-password" }), Ct);
        Assert.Equal(HttpStatusCode.Found, sent.StatusCode);
        var callback = sent.Headers.Location!;
        Assert.Equal(Callback, callback.GetLeftPart(UriPartial.Path));
        var values = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(callback.Query);
        Assert.Equal(state, values["state"].ToString());
        Assert.Equal(fixture.Authority, values["iss"].ToString());
        var code = values["code"].ToString();
        Assert.NotEmpty(code);
        using var tokens = await browser.PostAsync(fixture.Authority + "/protocol/openid-connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["client_id"] = clientId, ["grant_type"] = "authorization_code", ["code"] = code,
                ["redirect_uri"] = Callback, ["code_verifier"] = verifier }), Ct);
        tokens.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await tokens.Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("access_token").GetString()!;
    }

    private Uri LoginAction(string html)
    {
        var action = Regex.Match(html, "<form[^>]*\\baction=\"([^\"]+)\"", RegexOptions.IgnoreCase).Groups[1].Value;
        Assert.NotEmpty(action);
        var form = new Uri(WebUtility.HtmlDecode(action));
        Assert.Equal(new Uri(fixture.Authority).GetLeftPart(UriPartial.Authority), form.GetLeftPart(UriPartial.Authority));
        return form;
    }

    // Browsers accept Secure cookies on localhost. HttpClient's CookieContainer does not apply that
    // loopback exception. Model it only for this exact fixture origin; keep cookie path/expiry rules,
    // refuse redirects and never relax production transport or cookie settings.
    private sealed class LoopbackBrowserCookies : DelegatingHandler
    {
        private readonly Uri _origin;
        private readonly CookieContainer _cookies = new();

        public LoopbackBrowserCookies(Uri origin)
            : base(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        {
            Assert.True(origin.IsLoopback && origin.Scheme == Uri.UriSchemeHttp);
            _origin = origin;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var target = request.RequestUri!;
            Assert.Equal(_origin.GetLeftPart(UriPartial.Authority), target.GetLeftPart(UriPartial.Authority));
            var cookieUri = new UriBuilder(target) { Scheme = Uri.UriSchemeHttps, Port = target.Port }.Uri;
            var header = _cookies.GetCookieHeader(cookieUri);
            if (header.Length > 0) request.Headers.Add("Cookie", header);
            var response = await base.SendAsync(request, ct);
            if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
                foreach (var cookie in cookies) _cookies.SetCookies(cookieUri, cookie);
            return response;
        }
    }

    private static async Task AssertAudienceRefusalAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;
        Assert.Equal("access_denied", error.GetProperty("error").GetString());
        Assert.Equal("Client is not within the token audience", error.GetProperty("error_description").GetString());
    }

    private async Task<HttpResponseMessage> ExchangeAsync(HttpClient http, string requester, string secret, string subject)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, fixture.Authority + "/protocol/openid-connect/token");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(requester + ":" + secret)));
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "urn:ietf:params:oauth:grant-type:token-exchange",
            ["subject_token"] = subject, ["subject_token_type"] = "urn:ietf:params:oauth:token-type:access_token",
            ["requested_token_type"] = "urn:ietf:params:oauth:token-type:access_token", ["audience"] = "billing", ["scope"] = "organization:firm-a domain-claims" });
        return await http.SendAsync(request, Ct);
    }
}
