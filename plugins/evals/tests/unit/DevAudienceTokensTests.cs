extern alias service;

using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Maf.Lab.Domain.Configuration;
using Maf.Lab.Domain.Tenancy;
using service::Maf.Lab.Eval.Hosting;
using Maf.Lab.Plugins.Abstractions;
using Maf.Lab.Retrieval.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Maf.Lab.Tests;

/// <summary>Only the harness credential HTTP boundary; no evaluator or model is started.</summary>
public sealed class DevAudienceTokensTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static Principal Persona(string user = "adam", string tenant = "firm-a") => new(user, TenantId.Firm(tenant), Role.USER);
    private static DevAudienceTokens Provider(HttpClient client, string? endpoint = null) => new(new Clients(client),
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Evals:GatewayBaseUrl"] = "http://localhost/",
            ["Evals:DevTokenEndpoint"] = endpoint,
        }).Build());
    private sealed class Clients(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal("dev-audience-tokens", name);
            return client;
        }
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    [Theory]
    [InlineData("adam", "firm-a")]
    [InlineData("chris", "firm-c")]
    public async Task Requests_each_audience_with_the_same_identity_and_never_forwards_or_exchanges_the_subject(string user, string tenant)
    {
        var requests = new List<string>();
        using var client = new HttpClient(new Handler(async (request, ct) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("http://issuer.test/custom-token", request.RequestUri!.AbsoluteUri);
            Assert.Null(request.Headers.Authorization);
            var body = await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
            Assert.Equal(["audience", "role", "tenantId", "userId"], body.EnumerateObject().Select(p => p.Name).Order());
            Assert.Equal(user, body.GetProperty("userId").GetString());
            Assert.Equal(tenant, body.GetProperty("tenantId").GetString());
            Assert.Equal("USER", body.GetProperty("role").GetString());
            var audience = body.GetProperty("audience").GetString()!;
            requests.Add(audience);
            return Json(JsonSerializer.Serialize(new { token = "issued-for-" + audience }));
        }));
        var provider = Provider(client, "http://issuer.test/custom-token");
        Assert.Equal("issued-for-api", await provider.MintAsync(Persona(user, tenant), "api", Ct));
        foreach (var plugin in new[] { "billing", "portfolio", "code" })
            Assert.Equal("issued-for-" + plugin, await provider.ForAsync(Persona(user, tenant), plugin, "SUBJECT-MUST-NOT-LEAVE", Ct));
        Assert.Equal(["api", "billing", "portfolio", "code"], requests);
    }

    [Fact]
    public async Task Snapshot_uses_its_fresh_api_bearer_and_only_the_returned_names()
    {
        var calls = 0;
        using var client = new HttpClient(new Handler((request, _) =>
        {
            calls++;
            if (calls == 1)
            {
                Assert.Equal("/dev/token", request.RequestUri!.AbsolutePath);
                return Task.FromResult(Json("{\"token\":\"fresh-api-token\"}"));
            }
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("http://localhost/api/plugins", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("fresh-api-token", request.Headers.Authorization.Parameter);
            return Task.FromResult(Json("{\"plugins\":[{\"name\":\"billing\"}],\"domains\":[{\"name\":\"foreign\"}]}"));
        }));
        Assert.Equal(["billing"], (await Provider(client).For(Persona(), Ct)).Names);
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{\"token\":null}")]
    [InlineData("{\"token\":42}")]
    [InlineData("{\"token\":\"\"}")]
    [InlineData("{\"token\":\"  \"}")]
    public async Task Malformed_token_shape_never_becomes_a_credential(string body)
    {
        using var client = new HttpClient(new Handler((_, _) => Task.FromResult(Json(body))));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Provider(client).MintAsync(Persona(), "billing", Ct));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{\"plugins\":null}")]
    [InlineData("{\"plugins\":[{\"name\":\"billing\"},{}]}")]
    [InlineData("{\"plugins\":[{\"name\":null}]}")]
    [InlineData("{\"plugins\":[{\"name\":\" \"}]}")]
    public async Task Malformed_snapshot_never_returns_partial_access(string body)
    {
        using var client = new HttpClient(new Handler((request, _) => Task.FromResult(Json(request.Method == HttpMethod.Post ? "{\"token\":\"api-token\"}" : body))));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Provider(client).For(Persona(), Ct));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Refused_or_invalid_json_replies_fail_closed(bool snapshot)
    {
        foreach (var status in new[] { HttpStatusCode.Forbidden, HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK })
        {
            using var client = new HttpClient(new Handler((request, _) => Task.FromResult(snapshot && request.Method == HttpMethod.Post
                ? Json("{\"token\":\"api-token\"}") : Json("invalid-json", status))));
            var provider = Provider(client);
            var error = await Record.ExceptionAsync(async () =>
            {
                if (snapshot) await provider.For(Persona(), Ct);
                else await provider.ForAsync(Persona(), "billing", "unused-subject", Ct);
            });
            Assert.NotNull(error);
            if (status == HttpStatusCode.OK) Assert.IsType<JsonException>(error);
            else if (snapshot) Assert.IsType<HttpRequestException>(error);
            else Assert.IsType<UnauthorizedAccessException>(error);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_reaches_issuer_and_snapshot_http(bool snapshot)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        using var client = new HttpClient(new Handler(async (request, ct) =>
        {
            if (snapshot && request.Method == HttpMethod.Post) return Json("{\"token\":\"api-token\"}");
            stop.Cancel();
            ct.ThrowIfCancellationRequested();
            await Task.CompletedTask;
            return Json("{}");
        }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            if (snapshot) await Provider(client).For(Persona(), stop.Token);
            else await Provider(client).ForAsync(Persona(), "billing", "unused-subject", stop.Token);
        });
    }

    private static PluginManifest Manifest(string name) => new()
    {
        Name = name, Kind = "mcp", Scope = "tenant", Environments = ["dev"], Description = "Credential fixture",
        Progress = "None — fixture", Stopping = "None — fixture",
    };

    [Theory]
    [InlineData("adam", "firm-a", "firm-c")]
    [InlineData("chris", "firm-c", "firm-a")]
    public async Task Actual_dev_issuer_mints_matching_audiences_and_snapshot_obeys_the_authenticated_tenant(string user, string tenant, string otherTenant)
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel())
        {
            BootstrapTenantPlugins = false,
            InstalledPlugins = [DevLoginPluginSupport.Manifest, Manifest("billing"), Manifest("portfolio"), Manifest("code")],
        };
        using var client = api.CreateClient();
        var store = api.Services.GetRequiredService<IPluginEntitlements>();
        var owner = new Principal("operator", TenantId.Firm(tenant), Role.PLATFORM_ADMIN);
        foreach (var plugin in new[] { "billing", "portfolio", "code" })
            await store.AllowAsync(owner, plugin, true, true, Ct);
        var provider = Provider(client);
        var principal = Persona(user, tenant);
        foreach (var audience in new[] { "api", "billing", "portfolio", "code" })
        {
            var token = audience == "api" ? await provider.MintAsync(principal, audience, Ct)
                : await provider.ForAsync(principal, audience, "SUBJECT-MUST-NOT-LEAVE", Ct);
            var validation = await new JsonWebTokenHandler().ValidateTokenAsync(token, DevJwt.ValidationParameters(new AuthOptions { Audience = audience }));
            Assert.True(validation.IsValid);
            Assert.True(PrincipalClaims.TryCreate(new ClaimsPrincipal(validation.ClaimsIdentity), out var actual));
            Assert.Equal(principal, actual);
        }
        await store.AllowAsync(new Principal("operator", TenantId.Firm(otherTenant), Role.PLATFORM_ADMIN), "portfolio", true, true, Ct);
        await store.EnableAsync(new Principal(user, TenantId.Firm(tenant), Role.TENANT_ADMIN), "portfolio", false, Ct);
        await store.AllowAsync(owner, "code", false, false, Ct);
        var snapshot = await provider.For(principal, Ct);
        Assert.True(snapshot.IsInUse("billing"));
        Assert.False(snapshot.IsInUse("portfolio"));
        Assert.False(snapshot.IsInUse("code"));
        foreach (var denied in new[] { "portfolio", "code" })
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => provider.ForAsync(principal, denied, "unused-subject", Ct));
    }
}
