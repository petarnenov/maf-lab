using Maf.Lab.Api.Agent;
using Maf.Lab.Plugins.Abstractions;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Api.Plugins;

/// <summary>The core's side of <see cref="IInstalledPlugins"/>: the catalogue's current set, with the agent's precedence.</summary>
public sealed class InstalledPlugins(PluginCatalogue catalogue, IOptions<AgentOptions> agent) : IInstalledPlugins
{
    public bool IsInstalled(string plugin) => catalogue.Current.Contains(plugin);

    public string? McpEndpoint(string plugin)
    {
        if (!IsInstalled(plugin))
        {
            return null;
        }
        // The same rule as AgentOptions.AllServers: a configured key shadows the manifest's server of that name.
        return agent.Value.Servers.TryGetValue(plugin, out var configured) && !string.IsNullOrWhiteSpace(configured.Endpoint)
            ? configured.Endpoint
            : catalogue.McpServers().GetValueOrDefault(plugin)?.Endpoint;
    }
}
