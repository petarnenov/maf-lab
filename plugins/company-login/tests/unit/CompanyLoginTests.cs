using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maf.Lab.Domain.Configuration;
using Maf.Lab.Plugins.Abstractions;
using Maf.Lab.Plugins.CompanyLogin;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Tests;

public sealed class CompanyLoginTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static PluginManifest Manifest => new() { Name = CompanyLoginPlugin.PluginName, Kind = PluginKinds.App,
        Scope = PluginScopes.Installation, Public = true, Environments = ["dev", "qa", "stage", "prod"],
        Description = "Company identity", Progress = "None — fixture", Stopping = "None — fixture" };

    [Fact]
    public async Task Configuration_is_anonymous_public_metadata_without_credentials_and_cannot_be_changed_by_request_parameters()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel())
        {
            InstalledPlugins = [Manifest], ServerWithoutCode = [CompanyLoginPlugin.PluginName],
            ExtraSettings = new Dictionary<string, string?> { ["Auth:Authority"] = "https://company.identity.invalid/realms/lab/",
                ["Auth:WebClientId"] = "web", ["TokenExchange:ClientSecret"] = "fixture-private-secret" },
        };
        using var client = api.CreateClient();
        var result = await client.GetFromJsonAsync<JsonElement>("/api/identity/configuration?authority=https://other.invalid&clientId=evil", Ct);
        Assert.Equal("https://company.identity.invalid/realms/lab", result.GetProperty("authority").GetString());
        Assert.Equal("web", result.GetProperty("clientId").GetString());
        Assert.Equal("code", result.GetProperty("responseType").GetString());
        Assert.Equal("/auth/callback", result.GetProperty("callbackPath").GetString());
        Assert.Equal("/auth/signed-out", result.GetProperty("signedOutPath").GetString());
        Assert.Contains("organization", result.GetProperty("scope").GetString());
        Assert.Equal(6, result.EnumerateObject().Count());
        Assert.DoesNotContain("secret", result.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", result.GetRawText(), StringComparison.OrdinalIgnoreCase);
        var plugins = await client.GetFromJsonAsync<JsonElement>("/api/plugins", Ct);
        Assert.Contains(plugins.GetProperty("plugins").EnumerateArray(), plugin => plugin.GetProperty("name").GetString() == CompanyLoginPlugin.PluginName);
    }

    [Fact]
    public async Task An_unconfigured_development_installation_has_no_active_company_configuration()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel()) { InstalledPlugins = [Manifest], ServerWithoutCode = [CompanyLoginPlugin.PluginName] };
        Assert.Equal(HttpStatusCode.NotFound, (await api.CreateClient().GetAsync("/api/identity/configuration", Ct)).StatusCode);
    }

    [Fact]
    public async Task Removing_the_plugin_removes_the_configuration_route()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel()) { InstalledPlugins = [], ExtraSettings = new Dictionary<string, string?> { ["Auth:Authority"] = "https://company.identity.invalid/realms/lab" } };
        Assert.Equal(HttpStatusCode.NotFound, (await api.CreateClient().GetAsync("/api/identity/configuration", Ct)).StatusCode);
    }

    [Fact]
    public void A_configured_company_sign_in_requires_a_public_client_id()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Auth:Authority"] = "https://company.identity.invalid/realms/lab", ["Auth:WebClientId"] = "" }).Build();
        Assert.Throws<InvalidOperationException>(() => { new CompanyLoginPlugin().ConfigureServices(new ServiceCollection(), configuration); });
    }
}
