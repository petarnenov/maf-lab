using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Plugins.Abstractions;
using Maf.Lab.Plugins.DevLogin;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Maf.Lab.Tests;

public sealed class DevLoginTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static ApiFactory Host()
    {
        var host = new ApiFactory(ApiFactory.ProceduralModel())
        {
            BootstrapTenantPlugins = false,
            InstalledPlugins = DevLoginPluginSupport.Installed,
        };
        host.ConfigureTestServices = services =>
        {
            services.RemoveAll<IPluginTokens>();
            services.AddSingleton<IPluginTokens, DevPluginTokens>();
        };
        return host;
    }
    private static async Task<string> Token(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/dev/token", body, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("token").GetString()!;
    }

    [Fact]
    public async Task Issuer_is_public_only_while_the_development_plugin_is_installed()
    {
        using var host = Host();
        var client = host.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/dev/users", Ct)).StatusCode);
        var listed = await client.GetFromJsonAsync<JsonElement>("/api/plugins", Ct);
        Assert.Contains(listed.GetProperty("plugins").EnumerateArray(), p => p.GetProperty("name").GetString() == "dev-login");
        host.SetInstalled(StandInDomains.Installed);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/dev/users", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/dev/token", new { persona = "adam" }, Ct)).StatusCode);
    }

    [Fact]
    public async Task Operator_tokens_carry_the_requested_organization_and_the_operators_own_identity()
    {
        using var host = Host();
        var token = new JsonWebToken(await Token(host.CreateClient(), new { persona = "operator", tenantId = "firm-b", audience = "api" }));
        Assert.Equal("operator", token.Subject);
        Assert.Equal(["api"], token.Audiences);
        Assert.Contains(token.Claims, c => c.Type == PrincipalClaims.TenantId && c.Value == "firm-b");
        Assert.Contains(token.Claims, c => c.Type == PrincipalClaims.Role && c.Value == "PLATFORM_ADMIN");
    }

    [Fact]
    public async Task Persona_keeps_domain_claims_and_cannot_change_tenant_or_role()
    {
        using var host = Host();
        var client = host.CreateClient();
        var token = new JsonWebToken(await Token(client, new { persona = "adam", audience = "api" }));
        Assert.Contains(token.Claims, c => c.Type == PrincipalClaims.DomainRoles && c.Value == "billing:advisor");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/dev/token", new { persona = "adam", tenantId = "firm-b" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/dev/token", new { persona = "adam", role = "PLATFORM_ADMIN" }, Ct)).StatusCode);
    }

    [Fact]
    public async Task Only_installed_enabled_audiences_can_be_minted_and_the_runtime_provider_preserves_claims()
    {
        using var host = Host();
        var client = host.CreateClient();
        var name = StandInDomains.BillingManifest.Name;
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/dev/token", new { persona = "adam", audience = name }, Ct)).StatusCode);
        await host.Services.GetRequiredService<IPluginEntitlements>().AllowAsync(
            new Principal("operator", TenantId.Firm("firm-a"), Role.PLATFORM_ADMIN), name, true, true, Ct);
        var audienceToken = new JsonWebToken(await Token(client, new { persona = "adam", audience = name }));
        Assert.Equal([name], audienceToken.Audiences);
        var subject = await Token(client, new { persona = "adam", audience = "api" });
        var exchanged = await host.Services.GetRequiredService<IPluginTokens>().ForAsync(
            new Principal("adam", TenantId.Firm("firm-a"), Role.USER), name, subject, Ct);
        Assert.Equal([name], new JsonWebToken(exchanged).Audiences);
        Assert.Contains(new JsonWebToken(exchanged).Claims, c => c.Type == PrincipalClaims.AdvisorIds && c.Value == "adv-a-1");
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/dev/token", new { persona = "adam", audience = "missing" }, Ct)).StatusCode);
    }

    [Theory]
    [InlineData("stage")]
    [InlineData("prod")]
    public void The_product_environment_refuses_the_dev_issuer(string environment)
    {
        using var host = new ApiFactory(ApiFactory.ProceduralModel()) { Environment = environment, InstalledPlugins = [DevLoginPluginSupport.Manifest] };
        var error = Assert.Throws<InvalidOperationException>(() => host.CreateClient());
        Assert.Contains($"plugin 'dev-login' is not allowed in MAF_ENV={environment}", error.ToString());
    }

    [Fact]
    public void Company_authority_refuses_installation_of_the_development_issuer()
    {
        var services = new ServiceCollection();
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Auth:Authority"] = "https://company.identity.invalid/realms/lab" }).Build();
        Assert.Throws<InvalidOperationException>(() => { new DevLoginPlugin().ConfigureServices(services, configuration); });
        Assert.DoesNotContain(services, service => service.ServiceType == typeof(IPluginTokens));
    }
}
