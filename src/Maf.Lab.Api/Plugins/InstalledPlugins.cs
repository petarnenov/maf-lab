using Maf.Lab.Api.Agent;
using Maf.Lab.Plugins.Abstractions;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Api.Plugins;

/// <summary>The core's side of <see cref="IInstalledPlugins"/>: the catalogue's current set, with the agent's precedence.</summary>
public sealed class InstalledPlugins(PluginCatalogue catalogue, IOptions<AgentOptions> agent, IOptions<PluginOptions> options) : IInstalledPlugins
{
    public bool IsInstalled(string plugin) => catalogue.Current.Contains(plugin);

    public string? McpEndpoint(string plugin)
    {
        if (!IsInstalled(plugin))
        {
            return null;
        }
        // The same rule as AgentOptions.AllServers: a configured key overrides what it sets of the manifest's server.
        return agent.Value.Servers.TryGetValue(plugin, out var configured) && !string.IsNullOrWhiteSpace(configured.Endpoint)
            ? configured.Endpoint
            : catalogue.McpServers().GetValueOrDefault(plugin)?.Endpoint;
    }

    public IReadOnlyList<PluginManifest> Installed() => [.. catalogue.Current.Plugins.Select(p => p.Manifest)];

    public string? AgentEndpoint(string plugin)
    {
        var card = catalogue.Current.Plugins.SingleOrDefault(p => p.Name == plugin)?.AgentCard;
        if (card?["supportedInterfaces"] is not System.Text.Json.Nodes.JsonArray interfaces)
        {
            return null;
        }
        return interfaces.FirstOrDefault(i => i?["protocolBinding"]?.GetValue<string>() == "JSONRPC")?["url"]?.GetValue<string>();
    }

    public IReadOnlyList<PluginCorpus> Corpora()
    {
        var root = Path.GetFullPath(options.Value.Root);
        var corpora = new List<PluginCorpus>();
        foreach (var plugin in catalogue.Current.Plugins)
        {
            if (plugin.Manifest.Corpus is not { } corpus)
            {
                continue;
            }
            var folder = Path.Combine(root, plugin.Name);
            var path = Path.GetFullPath(Path.Combine(folder, corpus.Path));
            // The schema keeps the path inside the folder; a manifest that escapes it anyway offers no corpus.
            if (!path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.Ordinal) && path != folder)
            {
                continue;
            }
            corpora.Add(new PluginCorpus(plugin.Name, path, corpus.Collection, corpus.MetaCollection, corpus.Layout, corpus.Graph));
        }
        return corpora;
    }
}
