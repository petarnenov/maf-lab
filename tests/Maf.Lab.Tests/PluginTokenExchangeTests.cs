using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Maf.Lab.Api.Plugins;
using Maf.Lab.Api.Agent;
using Microsoft.Extensions.Logging.Abstractions;
using Maf.Lab.Domain.Configuration;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Plugins.Abstractions;
using Maf.Lab.Retrieval.Auth;
using Maf.Lab.TestSupport;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Maf.Lab.Tests;

public sealed class PluginTokenExchangeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly AuthOptions Auth = new() { Audience = "api" };
    private static string Subject(Principal principal, TimeProvider clock) => DevJwt.Issue(Auth, principal.UserId, principal.TenantId, principal.Role,
        clock.GetUtcNow(), ["weather:reader"], ["adv-1"]).Token;

    private static PluginTokenExchange Exchange(TenantPluginFixture fixture, IPluginAccess access, Handler handler) => new(access, fixture.Catalogue,
        new Clients(handler), Options.Create(new PluginTokenExchangeOptions { TokenEndpoint = "http://localhost/token", ClientId = "api", ClientSecret = "fixture-client-secret" }),
        new AuthenticationOptions(), fixture.Clock);

    [Fact]
    public async Task Exchange_sends_RFC8693_form_and_uses_one_plugin_audience_without_passthrough()
    {
        await using var f = new TenantPluginFixture(new FakeTimeProvider(DateTimeOffset.UtcNow));
        await f.InitializeAsync(Ct);
        var changes = new PluginAccessChanges(Microsoft.Extensions.Logging.Abstractions.NullLogger<PluginAccessChanges>.Instance);
        var store = f.Store(changes);
        await store.AllowAsync(TenantPluginFixture.Operator, "weather", true, true, Ct);
        var handler = new Handler(f.Clock);
        var exchange = Exchange(f, f.Access(store, changes), handler);
        var subject = Subject(TenantPluginFixture.User, f.Clock);
        var token = await exchange.ForAsync(TenantPluginFixture.User, "weather", subject, Ct);
        Assert.NotEqual(subject, token);
        Assert.Equal(["weather"], new JsonWebToken(token).Audiences);
        Assert.Equal("urn:ietf:params:oauth:grant-type:token-exchange", handler.LastForm!["grant_type"]);
        Assert.Equal("weather", handler.LastForm["audience"]);
        Assert.Equal(subject, handler.LastForm["subject_token"]);
        Assert.Equal("api:fixture-client-secret", Encoding.UTF8.GetString(Convert.FromBase64String(handler.Basic!)));
        Assert.Equal("firm-a", new JsonWebToken(token).Claims.Single(c => c.Type == PrincipalClaims.TenantId).Value);
    }

    [Fact]
    public async Task A_cached_token_never_bypasses_a_new_permission_snapshot_and_expires_at_its_deadline()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var f = new TenantPluginFixture(clock);
        await f.InitializeAsync(Ct);
        var bus = new PluginAccessChanges(Microsoft.Extensions.Logging.Abstractions.NullLogger<PluginAccessChanges>.Instance);
        var store = f.Store(bus);
        await store.AllowAsync(TenantPluginFixture.Operator, "weather", true, true, Ct);
        var handler = new Handler(clock);
        var exchange = Exchange(f, f.Access(store, bus), handler);
        var subject = Subject(TenantPluginFixture.User, clock);
        var first = await exchange.ForAsync(TenantPluginFixture.User, "weather", subject, Ct);
        Assert.Equal(first, await exchange.ForAsync(TenantPluginFixture.User, "weather", subject, Ct));
        Assert.Equal(1, handler.Requests);
        clock.Advance(TimeSpan.FromSeconds(30));
        await exchange.ForAsync(TenantPluginFixture.User, "weather", subject, Ct);
        Assert.Equal(2, handler.Requests);
        await store.AllowAsync(TenantPluginFixture.Operator, "weather", false, false, Ct);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => exchange.ForAsync(TenantPluginFixture.User, "weather", subject, Ct));
        Assert.Equal(2, handler.Requests);
    }

    [Theory]
    [InlineData("wrong-audience")]
    [InlineData("wrong-tenant")]
    [InlineData("wrong-signature")]
    [InlineData("lost-claims")]
    [InlineData("refused")]
    [InlineData("redirect")]
    [InlineData("passthrough")]
    public async Task Invalid_or_refused_exchanges_never_yield_a_token(string fault)
    {
        await using var f = new TenantPluginFixture(new FakeTimeProvider(DateTimeOffset.UtcNow));
        await f.InitializeAsync(Ct);
        var exchange = Exchange(f, new Allowed(), new Handler(f.Clock) { Fault = fault });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => exchange.ForAsync(TenantPluginFixture.User, "weather", Subject(TenantPluginFixture.User, f.Clock), Ct));
    }

    [Fact]
    public async Task Cache_is_separate_for_user_plugin_and_subject_credentials_and_collapses_concurrent_requests()
    {
        await using var f = new TenantPluginFixture(new FakeTimeProvider(DateTimeOffset.UtcNow));
        await f.InitializeAsync(Ct);
        var handler = new Handler(f.Clock);
        var exchange = Exchange(f, new Allowed(), handler);
        var subject = Subject(TenantPluginFixture.User, f.Clock);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => exchange.ForAsync(TenantPluginFixture.User, "weather", subject, Ct)));
        Assert.Equal(1, handler.Requests);
        await exchange.ForAsync(TenantPluginFixture.User, "history", subject, Ct);
        var another = TenantPluginFixture.User with { UserId = "another" };
        await exchange.ForAsync(another, "weather", Subject(another, f.Clock), Ct);
        Assert.Equal(3, handler.Requests);
    }

    [Fact]
    public async Task The_official_MCP_client_sends_the_exchanged_token_instead_of_the_subject_token()
    {
        await using var f = new TenantPluginFixture(new FakeTimeProvider(DateTimeOffset.UtcNow));
        await f.InitializeAsync(Ct);
        var handler = new Handler(f.Clock);
        var exchange = Exchange(f, new Allowed(), handler);
        var subject = Subject(TenantPluginFixture.User, f.Clock);
        var source = new McpToolSource(Options.Create(new AgentOptions { Servers = new() {
            ["weather"] = new McpServerOptions { Plugin = "weather", Domain = "billing", Endpoint = "http://localhost/mcp" },
        } }), NullLoggerFactory.Instance, new Clients(handler), domainCatalogue: StandInDomains.WithBilling, tokens: exchange);
        using var caller = PluginAccessContext.Use(TenantPluginFixture.User, new PluginAccessSnapshot(["weather"]));
        await using var tools = await source.GetToolsAsync(subject, null, Ct);
        Assert.Equal(["fixture_tool"], tools.Names);
        Assert.NotEmpty(handler.McpTokens);
        Assert.All(handler.McpTokens, token => {
            Assert.NotEqual(subject, token);
            Assert.Equal(["weather"], new JsonWebToken(token).Audiences);
        });
    }

    [Fact]
    public async Task Cancellation_aborts_the_exchange_request_and_does_not_cache_it()
    {
        await using var f = new TenantPluginFixture(new FakeTimeProvider(DateTimeOffset.UtcNow));
        await f.InitializeAsync(Ct);
        var handler = new Handler(f.Clock) { Fault = "hold" };
        var exchange = Exchange(f, new Allowed(), handler);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var subject = Subject(TenantPluginFixture.User, f.Clock);
        var pending = exchange.ForAsync(TenantPluginFixture.User, "weather", subject, stop.Token);
        await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        handler.Fault = null;
        await exchange.ForAsync(TenantPluginFixture.User, "weather", subject, Ct);
        Assert.Equal(2, handler.Requests);
    }

    private sealed class Allowed : IPluginAccess
    {
        public Task<PluginAccessSnapshot> For(Principal principal, CancellationToken ct) => Task.FromResult(new PluginAccessSnapshot(["weather", "history"]));
    }
    private sealed class Clients(Handler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
    private sealed class AuthenticationOptions : IOptionsMonitor<JwtBearerOptions>
    {
        public JwtBearerOptions CurrentValue => Get(null);
        public JwtBearerOptions Get(string? name) => new() { TokenValidationParameters = DevJwt.ValidationParameters(Auth) };
        public IDisposable? OnChange(Action<JwtBearerOptions, string?> listener) => null;
    }
    private sealed class Handler(TimeProvider clock) : HttpMessageHandler
    {
        public int Requests;
        public string? Fault;
        public Dictionary<string, string>? LastForm;
        public string? Basic;
        public List<string> McpTokens { get; } = [];
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath == "/mcp")
            {
                McpTokens.Add(request.Headers.Authorization!.Parameter!);
                var message = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement;
                var method = message.GetProperty("method").GetString();
                object result = method == "server/discover" ? new { supportedVersions = new[] { "2026-07-28" }, capabilities = new { tools = new { } }, ttlMs = 0, cacheScope = "private" }
                    : new { tools = new[] { new { name = "fixture_tool", inputSchema = new { type = "object", properties = new { } } } }, ttlMs = 0, cacheScope = "private" };
                return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = message.GetProperty("id"), result }), Encoding.UTF8, "application/json") };
            }
            Interlocked.Increment(ref Requests);
            Basic = request.Headers.Authorization?.Parameter;
            var form = (await request.Content!.ReadAsStringAsync(ct)).Split('&').Select(pair => pair.Split('=', 2))
                .ToDictionary(pair => Uri.UnescapeDataString(pair[0].Replace('+', ' ')), pair => Uri.UnescapeDataString(pair[1].Replace('+', ' ')));
            LastForm = form;
            if (Fault == "hold") { Entered.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); }
            if (Fault == "refused") return new(HttpStatusCode.BadRequest);
            if (Fault == "redirect") return new(HttpStatusCode.TemporaryRedirect)
            {
                Headers = { Location = new Uri("https://another-origin.invalid/token") },
            };
            var original = new JsonWebToken(form["subject_token"]);
            var auth = new AuthOptions { Audience = Fault == "wrong-audience" ? "api" : form["audience"], SigningKey = Fault == "wrong-signature" ? new string('x', 40) : Auth.SigningKey };
            var token = Fault == "passthrough" ? form["subject_token"] : DevJwt.Issue(auth, original.Subject,
                TenantId.Firm(Fault == "wrong-tenant" ? "firm-b" : "firm-a"), Role.USER, clock.GetUtcNow(),
                Fault == "lost-claims" ? [] : ["weather:reader"], ["adv-1"]).Token;
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new
            {
                access_token = token, token_type = "Bearer", issued_token_type = "urn:ietf:params:oauth:token-type:access_token", expires_in = 30,
            }), Encoding.UTF8, "application/json") };
        }
    }
}
