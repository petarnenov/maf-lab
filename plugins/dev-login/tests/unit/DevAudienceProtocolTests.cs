extern alias service;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Plugins.Abstractions;
using Maf.Lab.TestSupport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;

namespace Maf.Lab.Tests;

/// <summary>Dev audience tokens cross the actual in-repo servers' official MCP Streamable HTTP transports.</summary>
public sealed class DevAudienceProtocolTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static PluginManifest Manifest(string name) => new()
    {
        Name = name, Kind = "mcp", Scope = "tenant", Environments = ["dev"], Description = "Protocol fixture",
        Progress = "None — fixture", Stopping = "None — fixture",
    };

    private static ApiFactory Issuer() => new(ApiFactory.ProceduralModel())
    {
        BootstrapTenantPlugins = false,
        InstalledPlugins = [DevLoginPluginSupport.Manifest, Manifest("billing"), Manifest("portfolio"), Manifest("code")],
    };

    private static async Task<string> Token(HttpClient issuer, string persona, string audience)
    {
        var response = await issuer.PostAsJsonAsync("/dev/token", new { persona, audience }, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("token").GetString()!;
    }

    private static async Task<McpClient> Connect(HttpClient http, string token)
    {
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(http.BaseAddress!, "/mcp"), TransportMode = HttpTransportMode.StreamableHttp,
        }, http, NullLoggerFactory.Instance, ownsHttpClient: true), cancellationToken: Ct);
    }

    private static WebApplicationFactory<T> Server<T>(string? seed = null) where T : class =>
        new WebApplicationFactory<T>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseFixtureEngine();
            builder.WithFakeSharedState();
            builder.WithoutCollectionBootstrap();
            builder.UseSetting("Qdrant:GrpcPort", "1");
            // A shared api setting cannot override the resource server's bound audience.
            builder.UseSetting("Auth:Audience", "api");
            if (seed is not null) builder.UseSetting("Billing:SeedPath", seed);
        });

    [Theory]
    [InlineData("adam", "firm-a")]
    [InlineData("chris", "firm-c")]
    public async Task Persona_tokens_initialize_and_list_tools_on_each_actual_server_and_fail_on_other_audiences(string persona, string tenant)
    {
        using var issuer = Issuer();
        using var tokens = issuer.CreateClient();
        var entitlements = issuer.Services.GetRequiredService<IPluginEntitlements>();
        foreach (var audience in new[] { "billing", "portfolio", "code" })
            await entitlements.AllowAsync(new Principal("operator", TenantId.Firm(tenant), Role.PLATFORM_ADMIN), audience, true, true, Ct);
        var apiToken = await Token(tokens, persona, "api");
        var billingToken = await Token(tokens, persona, "billing");
        var portfolioToken = await Token(tokens, persona, "portfolio");
        var codeToken = await Token(tokens, persona, "code");
        using var seed = new BillingSeedFixture();
        await using var billing = Server<Maf.Lab.Retrieval.Program>(seed.Path);
        await using var portfolio = Server<Maf.Lab.Portfolio.Program>();
        await using var code = Server<service::Maf.Lab.CodeSearch.Program>();
        await using var billingClient = await Connect(billing.CreateDefaultClient(), billingToken);
        await using var portfolioClient = await Connect(portfolio.CreateDefaultClient(), portfolioToken);
        await using var codeClient = await Connect(code.CreateDefaultClient(), codeToken);
        Assert.Contains(await billingClient.ListToolsAsync(cancellationToken: Ct), tool => tool.Name == "get_billing_run_status");
        Assert.Contains(await portfolioClient.ListToolsAsync(cancellationToken: Ct), tool => tool.Name == "get_household_portfolio");
        Assert.Contains(await codeClient.ListToolsAsync(cancellationToken: Ct), tool => tool.Name == "search_codebase");
        var ownRun = tenant == "firm-a" ? "4417" : "6617";
        var otherRun = tenant == "firm-a" ? "6617" : "4417";
        var own = await billingClient.CallToolAsync("get_billing_run_status", new Dictionary<string, object?> { ["runId"] = ownRun }, cancellationToken: Ct);
        Assert.NotEqual(true, own.IsError);
        Assert.Equal(ownRun, own.StructuredContent!.Value.GetProperty("runId").GetString());
        var foreign = await billingClient.CallToolAsync("get_billing_run_status", new Dictionary<string, object?> { ["runId"] = otherRun }, cancellationToken: Ct);
        Assert.Equal(true, foreign.IsError);
        await Assert.ThrowsAsync<HttpRequestException>(() => Connect(billing.CreateDefaultClient(), apiToken));
        await Assert.ThrowsAsync<HttpRequestException>(() => Connect(portfolio.CreateDefaultClient(), apiToken));
        await Assert.ThrowsAsync<HttpRequestException>(() => Connect(code.CreateDefaultClient(), apiToken));
        await Assert.ThrowsAsync<HttpRequestException>(() => Connect(billing.CreateDefaultClient(), portfolioToken));
        await Assert.ThrowsAsync<HttpRequestException>(() => Connect(portfolio.CreateDefaultClient(), codeToken));
        await Assert.ThrowsAsync<HttpRequestException>(() => Connect(code.CreateDefaultClient(), billingToken));
    }

    private sealed class BillingSeedFixture : IDisposable
    {
        public string Path { get; } = System.IO.Path.GetTempFileName();
        public BillingSeedFixture() => File.WriteAllText(Path, """
            [{"firmId":"firm-a","runId":"4417","status":"failed","periodStart":"2026-06-01","periodEnd":"2026-06-30",
              "accountCount":10,"failureReason":"FS-REQUIRED","updatedAt":"2026-07-01T00:00:00Z"},
             {"firmId":"firm-c","runId":"6617","status":"completed","periodStart":"2026-06-01","periodEnd":"2026-06-30",
              "accountCount":20,"failureReason":null,"updatedAt":"2026-07-01T00:00:00Z"}]
            """);
        public void Dispose() => File.Delete(Path);
    }

    [Theory]
    [InlineData("billing")]
    [InlineData("portfolio")]
    public async Task A_disabled_tenant_cannot_mint_a_server_token_or_use_its_api_token_to_call_the_actual_server_directly(string audience)
    {
        using var issuer = Issuer();
        using var tokens = issuer.CreateClient();
        await issuer.Services.GetRequiredService<IPluginEntitlements>().AllowAsync(
            new Principal("operator", TenantId.Firm("firm-c"), Role.PLATFORM_ADMIN), audience, true, true, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, (await tokens.PostAsJsonAsync("/dev/token", new { persona = "adam", audience }, Ct)).StatusCode);
        var apiToken = await Token(tokens, "adam", "api");
        await using var billing = Server<Maf.Lab.Retrieval.Program>();
        await using var portfolio = Server<Maf.Lab.Portfolio.Program>();
        HttpClient ResourceClient() => audience == "billing" ? billing.CreateDefaultClient() : portfolio.CreateDefaultClient();
        using var denied = ResourceClient();
        denied.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await denied.PostAsync("/mcp", null, Ct)).StatusCode);
        await using var enabled = await Connect(ResourceClient(), await Token(tokens, "chris", audience));
        Assert.NotEmpty(await enabled.ListToolsAsync(cancellationToken: Ct));
    }
}
