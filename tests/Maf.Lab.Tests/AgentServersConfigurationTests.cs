using System.Text.Json;
using System.Text.RegularExpressions;
using Maf.Lab.Api.Agent;
using Microsoft.Extensions.Configuration;

namespace Maf.Lab.Tests;

/// <summary>
/// The agent's domain servers are configured twice: <c>appsettings.json</c> for <c>make dev</c> and the compose file's
/// <c>Agent__Servers__N__*</c> environment for Docker. .NET configuration merges arrays by index, so the environment
/// overrides <c>Domain</c> and <c>Endpoint</c> of entry N but inherits whatever else entry N holds in the JSON — a
/// <c>Tools</c> allowlist meant for another domain included. That is how the Bulgarian history server once came up
/// behind the codebase server's allowlist and its only tool was dropped without a word (add-bulgarian-history-domain).
/// </summary>
public class AgentServersConfigurationTests
{
    private static string Repo => CorpusLoaderTests.RepoRoot();

    private static IReadOnlyDictionary<string, string?> ComposeApiEnvironment()
    {
        // The api service's environment block, as compose would hand it to the container: only the Agent__ keys matter.
        var compose = File.ReadAllText(Path.Combine(Repo, "compose", "docker-compose.yml"));
        var api = Regex.Match(compose, @"^  api:\n(?<body>(?:^(?:    .*|\s*)\n)+)", RegexOptions.Multiline).Groups["body"].Value;
        Assert.False(string.IsNullOrWhiteSpace(api), "the compose file has an api service");
        return Regex.Matches(api, @"^\s+(?<key>Agent__\S+):\s*(?<value>\S+)\s*$", RegexOptions.Multiline)
            .ToDictionary(m => m.Groups["key"].Value.Replace("__", ":"), m => (string?)m.Groups["value"].Value);
    }

    private static List<McpServerOptions> AppSettingsServers()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Repo, "src", "Maf.Lab.Api", "appsettings.json")));
        return json.RootElement.GetProperty("Agent").GetProperty("Servers").EnumerateArray()
            .Select(s => new McpServerOptions
            {
                Domain = s.GetProperty("Domain").GetString() ?? "",
                Endpoint = s.GetProperty("Endpoint").GetString() ?? "",
                Tools = s.TryGetProperty("Tools", out var t) ? [.. t.EnumerateArray().Select(x => x.GetString() ?? "")] : [],
            }).ToList();
    }

    [Fact]
    public void Compose_and_appsettings_name_the_same_domain_at_every_server_index()
    {
        var env = ComposeApiEnvironment();
        var servers = AppSettingsServers();
        var composeDomains = env.Where(kv => Regex.IsMatch(kv.Key, @"^Agent:Servers:\d+:Domain$"))
            .ToDictionary(kv => int.Parse(kv.Key.Split(':')[2]), kv => kv.Value);

        Assert.NotEmpty(composeDomains);
        Assert.Equal(servers.Count, composeDomains.Count);
        foreach (var (index, domain) in composeDomains)
        {
            Assert.True(index < servers.Count, $"compose names Agent__Servers__{index} but appsettings.json has {servers.Count} servers");
            Assert.True(servers[index].Domain == domain,
                $"Agent:Servers[{index}] is '{domain}' in compose but '{servers[index].Domain}' in appsettings.json — "
                + ".NET merges the two by index, so the Docker entry would inherit the JSON entry's Tools allowlist");
        }
    }

    [Fact]
    public void Merged_configuration_keeps_a_tools_allowlist_only_on_the_codebase_server()
    {
        // Bind exactly as Program.cs does, with the compose environment layered over appsettings.json.
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(Repo, "src", "Maf.Lab.Api", "appsettings.json"))
            .AddInMemoryCollection(ComposeApiEnvironment())
            .Build();
        var options = new AgentOptions();
        configuration.GetSection(AgentOptions.Section).Bind(options);

        var byDomain = options.AllServers().ToDictionary(s => s.Domain);
        Assert.Equal([Domains.Billing, Domains.BulgarianHistory, Domains.Codebase, Domains.Portfolio], byDomain.Keys.Order());
        Assert.Contains("http://lb/bulgarian-history/mcp", byDomain[Domains.BulgarianHistory].Endpoint);
        Assert.Empty(byDomain[Domains.BulgarianHistory].Tools);
        Assert.Empty(byDomain[Domains.Portfolio].Tools);
        Assert.Equal(["change_impact", "search_codebase", "trace_code_symbol"], byDomain[Domains.Codebase].Tools.Order());
    }
}
