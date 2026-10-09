using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maf.Lab.Api.Plugins;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Plugins.Abstractions;
using Maf.Lab.Tests.Plugins;
using Maf.Lab.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Tests;

/// <summary>
/// The composition root's plugin step and the plugin routes (introduce-plugins tasks 3.2, 3.4, 3.8): the api boots with
/// no plugin and with every plugin; only an installed plugin registers anything; the gate answers 404 the moment a
/// plugin leaves the installed set; an environment the plugin does not allow stops the start, naming it.
/// </summary>
[Collection(PluginFixtureCollection.Name)]
public class PluginHostTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task With_no_plugin_the_api_boots_and_registers_nothing_of_one()
    {
        // No plugin but the core's minimum providers (introduce-provider-plugins 5x): its decision engine.
        using var factory = new ApiFactory(ApiFactory.ProceduralModel()) { InstalledPlugins = [] };
        var client = factory.ClientFor("adam", "firm-a", Role.USER);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/fixture/ping", Ct)).StatusCode);
        Assert.Null(factory.Services.GetService<FixtureMarker>());
        var list = await client.GetFromJsonAsync<JsonElement>("/api/plugins", Json, Ct);
        var only = Assert.Single(list.GetProperty("plugins").EnumerateArray());
        Assert.Equal("provider", only.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task With_every_plugin_the_api_boots_and_the_plugin_serves()
    {
        using var factory = new ApiFactory(ApiFactory.ProceduralModel()) { InstalledPlugins = [FixturePlugin.Manifest()] };
        var client = factory.ClientFor("adam", "firm-a", Role.USER);

        var ping = await client.GetFromJsonAsync<JsonElement>("/api/fixture/ping", Json, Ct);
        Assert.Equal("pong", ping.GetProperty("pong").GetString());
        var list = await client.GetFromJsonAsync<JsonElement>("/api/plugins", Json, Ct);
        var plugin = Assert.Single(list.GetProperty("plugins").EnumerateArray(), p => p.GetProperty("kind").GetString() != "provider");
        Assert.Equal("fixture", plugin.GetProperty("name").GetString());
        Assert.Equal("ok", plugin.GetProperty("health").GetString());
    }

    [Fact]
    public async Task A_plugin_that_leaves_the_installed_set_answers_404_at_once()
    {
        using var factory = new ApiFactory(ApiFactory.ProceduralModel()) { InstalledPlugins = [FixturePlugin.Manifest()] };
        var client = factory.ClientFor("adam", "firm-a", Role.USER);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/fixture/ping", Ct)).StatusCode);

        factory.SetInstalled([]);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/fixture/ping", Ct)).StatusCode);
    }

    [Fact]
    public async Task Before_sign_in_only_public_plugins_are_listed()
    {
        using var factory = new ApiFactory(ApiFactory.ProceduralModel())
        {
            InstalledPlugins = [FixturePlugin.Manifest(), FixturePlugin.Manifest() with { Name = "dev-login", Public = true }],
        };

        var list = await factory.CreateClient().GetFromJsonAsync<JsonElement>("/api/plugins", Json, Ct);

        var names = list.GetProperty("plugins").EnumerateArray().Select(p => p.GetProperty("name").GetString()).ToList();
        Assert.Equal(["dev-login"], names);
    }

    [Fact]
    public void A_plugin_the_environment_does_not_allow_stops_the_start_naming_it()
    {
        using var factory = new ApiFactory(ApiFactory.ProceduralModel()) { InstalledPlugins = [FixturePlugin.Manifest("dev", "qa")], Environment = "prod" };

        var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("plugin 'fixture' is not allowed in MAF_ENV=prod", Flatten(error));
    }

    [Fact]
    public void An_installed_server_part_without_its_code_stops_the_start_naming_it()
    {
        var manifest = FixturePlugin.Manifest() with { Name = "zz-no-code" };
        using var factory = new ApiFactory(ApiFactory.ProceduralModel())
        {
            InstalledPlugins = [manifest],
            ServerWithoutCode = ["zz-no-code"],
        };

        var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("plugin 'zz-no-code' is installed with a server part, but this image has no code for it", Flatten(error));
    }

    [Fact]
    public async Task Open_work_is_listed_for_an_admin_and_stopped_through_the_plugin()
    {
        lock (FixturePlugin.Open)
        {
            FixturePlugin.Open.Clear();
            FixturePlugin.Open.Add(new OpenWorkItem("job", "j-1", "running"));
        }
        using var factory = new ApiFactory(ApiFactory.ProceduralModel()) { InstalledPlugins = [FixturePlugin.Manifest()] };
        var admin = factory.ClientFor("alice", "firm-a", Role.TENANT_ADMIN);
        var user = factory.ClientFor("adam", "firm-a", Role.USER);

        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/plugins/fixture/open-work", Ct)).StatusCode);
        var open = await admin.GetFromJsonAsync<OpenWorkItem[]>("/api/plugins/fixture/open-work", Json, Ct);
        Assert.Equal("j-1", Assert.Single(open!).Id);

        Assert.Equal(HttpStatusCode.Accepted, (await admin.PostAsync("/api/plugins/fixture/open-work/cancel", null, Ct)).StatusCode);
        Assert.Empty((await admin.GetFromJsonAsync<OpenWorkItem[]>("/api/plugins/fixture/open-work", Json, Ct))!);
    }

    [Fact]
    public void A_malformed_installed_file_keeps_the_last_good_set_and_says_so()
    {
        using var factory = new ApiFactory(ApiFactory.ProceduralModel()) { InstalledPlugins = [FixturePlugin.Manifest()] };
        _ = factory.CreateClient();
        var catalogue = factory.Services.GetRequiredService<PluginCatalogue>();

        File.WriteAllText(catalogue.InstalledPath, "{ not json");
        var set = catalogue.Refresh();

        Assert.True(set.Contains("fixture"));
        Assert.Contains(set.Problems, p => p.Contains(".installed could not be read"));
    }

    [Fact]
    public void A_malformed_manifest_leaves_out_only_that_plugin()
    {
        var set = PluginCatalogue.Parse("""
            {"schema":1,"env":"dev","plugins":[
              {"manifest":{"name":"good","kind":"app","environments":["dev"]}},
              {"manifest":{"name":"","kind":"app"}}]}
            """);

        Assert.Equal(["good"], set.Plugins.Select(p => p.Name));
        Assert.Single(set.Problems);
    }

    [Fact]
    public void A_configured_endpoint_over_an_installed_plugin_keeps_the_manifests_domain_and_tools()
    {
        var options = new Maf.Lab.Api.Agent.AgentOptions();
        options.Servers["weather"] = new Maf.Lab.Api.Agent.McpServerOptions { Endpoint = "http://localhost:5099/mcp" };
        var plugins = new Dictionary<string, Maf.Lab.Api.Agent.McpServerOptions>
        {
            ["weather"] = new() { Domain = "weather", Endpoint = "http://lb/weather/mcp", Tools = ["forecast"] },
        };

        var server = Assert.Single(options.AllServers(plugins));

        Assert.Equal(("weather", "http://localhost:5099/mcp"), (server.Domain, server.Endpoint));
        Assert.Equal(["forecast"], server.Tools);
    }

    [Fact]
    public void The_indexed_and_the_keyed_server_settings_both_bind()
    {
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Agent:Servers:0:Domain"] = "portfolio",
            ["Agent:Servers:0:Endpoint"] = "http://lb/portfolio/mcp",
            ["Agent:Servers:codebase:Domain"] = "codebase",
            ["Agent:Servers:codebase:Endpoint"] = "http://lb/code/mcp",
        }).Build();
        var options = new Maf.Lab.Api.Agent.AgentOptions();
        configuration.GetSection("Agent").Bind(options);

        // No server is implied any more (introduce-plugins 4.6): billing is configured like the others.
        Assert.Equal(["codebase", "portfolio"], options.AllServers().Select(s => s.Domain).Order(StringComparer.Ordinal));

        var withBilling = new Maf.Lab.Api.Agent.AgentOptions();
        new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Agent:Servers:0:Domain"] = "portfolio",
            ["Agent:Servers:0:Endpoint"] = "http://lb/portfolio/mcp",
            ["Agent:Servers:billing:Domain"] = "billing",
            ["Agent:Servers:billing:Endpoint"] = "http://lb/mcp",
            ["Agent:Servers:codebase:Domain"] = "codebase",
            ["Agent:Servers:codebase:Endpoint"] = "http://lb/code/mcp",
        }).Build().GetSection("Agent").Bind(withBilling);
        Assert.Equal(["billing", "codebase", "portfolio"], withBilling.AllServers().Select(s => s.Domain).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void An_installed_mcp_plugin_adds_its_server_after_the_configured_ones()
    {
        var set = PluginCatalogue.Parse("""
            {"schema":1,"env":"dev","plugins":[{"manifest":{"name":"weather","kind":"mcp","environments":["dev"],
              "domain":{"id":"weather","tools":["forecast"]}},
              "serverJson":{"name":"io.example/weather","remotes":[{"type":"streamable-http","url":"http://lb/weather/mcp"}]}}]}
            """);
        var plugin = Assert.Single(set.Plugins);
        Assert.Equal("http://lb/weather/mcp", plugin.McpEndpoint);
    }

    private static string Flatten(Exception e) => e.InnerException is { } inner ? e.Message + " | " + Flatten(inner) : e.Message;
}

/// <summary>The fixture's open work is static; tests that use it run one at a time.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PluginFixtureCollection
{
    public const string Name = "plugin fixture";
}
