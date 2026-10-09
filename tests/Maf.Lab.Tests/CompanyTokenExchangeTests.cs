using System.Collections.Frozen;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Maf.Lab.Api.Plugins;
using Maf.Lab.Domain.Configuration;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Retrieval.Auth;
using Maf.Lab.TestSupport;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Maf.Lab.Tests;

public sealed class CompanyTokenExchangeTests
{
    private const string Issuer = "https://company.identity.invalid/realms/lab";
    private const string CoreRoleClient = "company-core";
    private const string AccessTokenType = "urn:ietf:params:oauth:token-type:access_token";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly Principal Caller = new("adam", TenantId.Firm("firm-a"), Role.USER)
    {
        GroupIds = new[] { "group-a-id", "group-b-id" }.ToFrozenSet(StringComparer.Ordinal),
    };

    [Theory]
    [InlineData(null)]
    [InlineData("changed-sid")]
    [InlineData("missing-sid")]
    [InlineData("array-sid")]
    public async Task Operator_exchange_retains_the_native_session_or_refuses_and_does_not_cache_it(string? fault)
    {
        await using var fixture = new TenantPluginFixture(new FakeTimeProvider(DateTimeOffset.UtcNow));
        await fixture.InitializeAsync(Ct);
        using var company = new CompanyIssuer(fixture.Clock) { Operator = true, ResponseFault = fault };
        var exchange = await ExchangeAsync(fixture, company);
        var actor = Caller with { Role = Role.PLATFORM_ADMIN };
        var subject = company.Token(["api"]);
        if (fault is not null)
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => exchange.ForAsync(actor, "weather", subject, Ct));
            company.ResponseFault = null;
        }
        var exchanged = new JsonWebToken(await exchange.ForAsync(actor, "weather", subject, Ct));
        Assert.Equal("operator-session", exchanged.GetClaim("sid").Value);
        Assert.Equal(fault is null ? 1 : 2, company.Requests);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task Company_exchange_preserves_normalized_identity_groups_and_domain_entitlements(bool clientRole, bool multipleSubjectAudiences, bool renamedGroupPath)
    {
        await using var fixture = new TenantPluginFixture(new FakeTimeProvider(DateTimeOffset.UtcNow));
        await fixture.InitializeAsync(Ct);
        using var company = new CompanyIssuer(fixture.Clock) { ClientRole = clientRole, ResponseFault = renamedGroupPath ? "renamed-org-path" : null };
        var exchange = await ExchangeAsync(fixture, company);
        var subject = company.Token(multipleSubjectAudiences ? ["account", "api"] : ["api"]);

        var token = await exchange.ForAsync(Caller, "weather", subject, Ct);

        Assert.NotEqual(subject, token);
        var exchanged = new JsonWebToken(token);
        Assert.Equal(["weather"], exchanged.Audiences);
        Assert.True(CompanyIdentityClaims.TryNormalize(new ClaimsPrincipal(new ClaimsIdentity(exchanged.Claims, "validated")), CoreRoleClient, out var normalized));
        Assert.True(PrincipalClaims.TryCreate(normalized, out var principal));
        Assert.Equal(Caller, principal);
        Assert.Equal(["billing:advisor", "weather:reader"], exchanged.Claims.Where(c => c.Type == PrincipalClaims.DomainRoles).Select(c => c.Value).Order(StringComparer.Ordinal));
        Assert.Equal(["adv-1", "adv-2"], exchanged.Claims.Where(c => c.Type == PrincipalClaims.AdvisorIds).Select(c => c.Value).Order(StringComparer.Ordinal));
        Assert.Equal(1, company.Requests);
        Assert.Equal("POST", company.Method);
        Assert.Equal(Issuer + "/protocol/openid-connect/token", company.Endpoint);
        Assert.Equal("application/x-www-form-urlencoded", company.ContentType);
        Assert.Equal("api:fixture-client-secret", company.BasicCredentials);
        Assert.Equal(new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:token-exchange",
            ["subject_token"] = subject,
            ["subject_token_type"] = AccessTokenType,
            ["requested_token_type"] = AccessTokenType,
            ["audience"] = "weather",
            ["scope"] = "organization:firm-a domain-claims",
        }, company.Form);
    }

    [Theory]
    [InlineData("signature")]
    [InlineData("issuer")]
    [InlineData("expired")]
    [InlineData("future")]
    [InlineData("id-token")]
    [InlineData("delegation")]
    [InlineData("organization")]
    [InlineData("role")]
    [InlineData("realm-groups")]
    [InlineData("lost-group-id")]
    [InlineData("subject")]
    [InlineData("audience")]
    public async Task Invalid_company_subject_is_rejected_before_contacting_the_token_endpoint(string fault)
    {
        await using var fixture = new TenantPluginFixture(new FakeTimeProvider(DateTimeOffset.UtcNow));
        await fixture.InitializeAsync(Ct);
        using var company = new CompanyIssuer(fixture.Clock);
        var exchange = await ExchangeAsync(fixture, company);
        var subject = company.Token(fault == "audience" ? ["account"] : ["api"], fault);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => exchange.ForAsync(Caller, "weather", subject, Ct));

        Assert.Equal(0, company.Requests);
    }

    [Theory]
    [InlineData("signature")]
    [InlineData("issuer")]
    [InlineData("expired")]
    [InlineData("future")]
    [InlineData("id-token")]
    [InlineData("delegation")]
    [InlineData("organization")]
    [InlineData("role")]
    [InlineData("realm-groups")]
    [InlineData("lost-group-id")]
    [InlineData("subject")]
    [InlineData("audience")]
    [InlineData("many-audiences")]
    [InlineData("lost-domain-roles")]
    [InlineData("lost-advisor-ids")]
    public async Task Invalid_company_exchange_response_is_rejected_and_never_cached(string fault)
    {
        await using var fixture = new TenantPluginFixture(new FakeTimeProvider(DateTimeOffset.UtcNow));
        await fixture.InitializeAsync(Ct);
        using var company = new CompanyIssuer(fixture.Clock) { ResponseFault = fault };
        var exchange = await ExchangeAsync(fixture, company);
        var subject = company.Token(["api"]);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => exchange.ForAsync(Caller, "weather", subject, Ct));

        Assert.Equal(1, company.Requests);
        company.ResponseFault = null;
        var valid = await exchange.ForAsync(Caller, "weather", subject, Ct);
        Assert.Equal(["weather"], new JsonWebToken(valid).Audiences);
        Assert.Equal(2, company.Requests);
    }

    [Theory]
    [InlineData("organization:* domain-claims")]
    [InlineData("organization:firm-b domain-claims")]
    public async Task Configuration_cannot_widen_or_change_the_validated_caller_organization(string scope)
    {
        await using var fixture = new TenantPluginFixture(new FakeTimeProvider(DateTimeOffset.UtcNow));
        await fixture.InitializeAsync(Ct);
        using var company = new CompanyIssuer(fixture.Clock);
        var exchange = await ExchangeAsync(fixture, company, scope);
        await Assert.ThrowsAsync<InvalidOperationException>(() => exchange.ForAsync(Caller, "weather", company.Token(["api"]), Ct));
        Assert.Equal(0, company.Requests);
    }

    private static async Task<PluginTokenExchange> ExchangeAsync(TenantPluginFixture fixture, CompanyIssuer company, string scope = "organization domain-claims")
    {
        var changes = new PluginAccessChanges(NullLogger<PluginAccessChanges>.Instance);
        var store = fixture.Store(changes);
        await store.AllowAsync(TenantPluginFixture.Operator, "weather", true, true, Ct);
        return new PluginTokenExchange(fixture.Access(store, changes), fixture.Catalogue, company,
            Options.Create(new PluginTokenExchangeOptions
            {
                TokenEndpoint = Issuer + "/protocol/openid-connect/token", ClientId = "api", ClientSecret = "fixture-client-secret", Scope = scope,
            }), company.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>(), fixture.Clock,
            company.Services.GetRequiredService<IOptions<AuthOptions>>());
    }

    private sealed class CompanyIssuer : HttpMessageHandler, IHttpClientFactory
    {
        private readonly RSA _rsa = RSA.Create(2048);
        private readonly RSA _rogue = RSA.Create(2048);
        private readonly TimeProvider _clock;
        public readonly ServiceProvider Services;
        public bool ClientRole { get; init; }
        public bool Operator { get; init; }
        public string? ResponseFault { get; set; }
        public int Requests { get; private set; }
        public string? Method { get; private set; }
        public string? Endpoint { get; private set; }
        public string? ContentType { get; private set; }
        public string? BasicCredentials { get; private set; }
        public Dictionary<string, string>? Form { get; private set; }

        public CompanyIssuer(TimeProvider clock)
        {
            _clock = clock;
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MAF_ENV"] = "stage", ["Auth:Authority"] = Issuer, ["Auth:CoreRoleClientId"] = CoreRoleClient,
            }).Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddLabAuthentication(configuration);
            var parameters = _rsa.ExportParameters(false);
            var jwks = new JsonWebKeySet(JsonSerializer.Serialize(new
            {
                keys = new[] { new { kty = "RSA", use = "sig", kid = "fixture", alg = "RS256",
                    n = Base64UrlEncoder.Encode(parameters.Modulus!), e = Base64UrlEncoder.Encode(parameters.Exponent!) } },
            }));
            var metadata = new OpenIdConnectConfiguration { Issuer = Issuer };
            foreach (var key in jwks.GetSigningKeys()) metadata.SigningKeys.Add(key);
            services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata));
            Services = services.BuildServiceProvider();
        }

        public string Token(string[] audiences, string? fault = null)
        {
            var claims = new Dictionary<string, object>
            {
                ["sub"] = fault == "subject" ? "another" : Caller.UserId,
                ["aud"] = audiences,
                ["typ"] = fault == "id-token" ? "ID" : "Bearer",
                ["organization"] = new Dictionary<string, object>
                {
                    [fault == "organization" ? "firm-b" : "firm-a"] = new
                    {
                        groups = fault == "renamed-org-path" ? new[] { "/Renamed" } : ["/Advisors", "/Advisors"],
                    },
                },
                ["groups"] = fault == "realm-groups" ? new[] { "other-group-id" } : fault == "lost-group-id" ? ["group-a-id"] : ["group-a-id", "group-b-id", "group-b-id"],
                // Conflicting legacy claims must be overridden by company organization and core role normalization.
                [PrincipalClaims.TenantId] = "firm-b", [PrincipalClaims.Role] = "PLATFORM_ADMIN",
                [PrincipalClaims.DomainRoles] = new[] { "weather:reader", "billing:advisor" },
                [PrincipalClaims.AdvisorIds] = new[] { "adv-2", "adv-1" },
                ["realm_access"] = new { roles = ClientRole ? new[] { "offline_access" } : new[] { fault == "role" ? "TENANT_ADMIN" : Operator ? "platform_operator" : "USER", "offline_access" } },
                ["resource_access"] = new Dictionary<string, object>
                {
                    [CoreRoleClient] = new { roles = ClientRole ? new[] { fault == "role" ? "TENANT_ADMIN" : "USER" } : Array.Empty<string>() },
                    ["untrusted-client"] = new { roles = new[] { "PLATFORM_ADMIN" } },
                },
            };
            if (Operator)
            {
                claims["scope"] = "organization:firm-a domain-claims";
                if (fault != "missing-sid") claims["sid"] = fault == "array-sid" ? (object)new[] { "operator-session" }
                    : fault == "changed-sid" ? "another-session" : "operator-session";
            }
            if (fault == "delegation") claims["act"] = new { sub = "other" };
            if (fault == "lost-domain-roles") claims.Remove(PrincipalClaims.DomainRoles);
            if (fault == "lost-advisor-ids") claims.Remove(PrincipalClaims.AdvisorIds);
            var now = _clock.GetUtcNow().UtcDateTime;
            return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Issuer = fault == "issuer" ? "https://another.identity.invalid/realms/lab" : Issuer,
                Claims = claims.ToDictionary(pair => pair.Key, pair => (object)JsonSerializer.SerializeToElement(pair.Value)), IssuedAt = now.AddMinutes(-10),
                NotBefore = fault == "future" ? now.AddMinutes(1) : now.AddMinutes(-10),
                Expires = fault == "expired" ? now.AddMinutes(-1) : now.AddMinutes(5),
                SigningCredentials = new SigningCredentials(new RsaSecurityKey(fault == "signature" ? _rogue : _rsa) { KeyId = "fixture" }, SecurityAlgorithms.RsaSha256),
            });
        }

        public HttpClient CreateClient(string name)
        {
            Assert.Equal(PluginTokenExchange.ClientName, name);
            return new HttpClient(this, disposeHandler: false);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            Method = request.Method.Method;
            Endpoint = request.RequestUri!.AbsoluteUri;
            ContentType = request.Content!.Headers.ContentType!.MediaType;
            Assert.Equal("Basic", request.Headers.Authorization!.Scheme);
            BasicCredentials = Encoding.UTF8.GetString(Convert.FromBase64String(request.Headers.Authorization.Parameter!));
            Form = (await request.Content.ReadAsStringAsync(cancellationToken)).Split('&').Select(pair => pair.Split('=', 2))
                .ToDictionary(pair => Uri.UnescapeDataString(pair[0].Replace('+', ' ')), pair => Uri.UnescapeDataString(pair[1].Replace('+', ' ')));
            var audiences = ResponseFault == "many-audiences" ? new[] { Form["audience"], "api" }
                : new[] { ResponseFault == "audience" ? "api" : Form["audience"] };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    access_token = Token(audiences, ResponseFault), token_type = "Bearer",
                    issued_token_type = AccessTokenType, expires_in = 60,
                }), Encoding.UTF8, "application/json"),
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Services.Dispose();
                _rsa.Dispose();
                _rogue.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
