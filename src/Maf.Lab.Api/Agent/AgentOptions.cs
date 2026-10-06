namespace Maf.Lab.Api.Agent;

public sealed class AgentOptions
{
    public const string Section = "Agent";

    /// <summary>
    /// The domains' MCP servers, keyed by name (introduce-plugins decision 3): `Agent__Servers__billing__Endpoint`. Every
    /// server is equal: none is first, and none serves the tools no other claims (task 4.6). The indexed form
    /// (`Agent__Servers__0__…`) still binds, its keys being "0", "1", for one change. A turn offers the union of every
    /// server's tools, each tool known by its domain.
    /// </summary>
    public Dictionary<string, McpServerOptions> Servers { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The built-in domains in use, comma-separated (introduce-plugins 5g, until each becomes a plugin folder): unset is
    /// all of them; empty is none — `make core`, which declines every turn (decision 5h).
    /// </summary>
    public string? BuiltInDomains { get; set; }

    /// <summary>The built-in domains' ids this deployment keeps.</summary>
    public IReadOnlyList<string> BuiltInDomainIds() => BuiltInDomains is null
        ? Maf.Lab.Api.BuiltIn.BuiltInDomains.Ids
        : [.. BuiltInDomains.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    /// <summary>
    /// Every server of the turn, in configuration order, then the installed MCP plugins' that no configured key already
    /// names. A configured key that names an installed plugin overrides only what it sets — typically just the endpoint
    /// (`make dev`'s local server) — and takes the domain and tools from the plugin's manifest. The first server to offer
    /// a tool name keeps it; a server whose domain is not in use is not contacted.
    /// </summary>
    public IReadOnlyList<McpServerOptions> AllServers(IReadOnlyDictionary<string, McpServerOptions>? plugins = null)
    {
        plugins ??= new Dictionary<string, McpServerOptions>();
        return [.. Servers.Select(s => plugins.TryGetValue(s.Key, out var plugin) ? Over(plugin, s.Value) : s.Value)
            .Concat(plugins.Where(p => !Servers.ContainsKey(p.Key)).Select(p => p.Value))
            .Where(s => !string.IsNullOrWhiteSpace(s.Endpoint) && !string.IsNullOrWhiteSpace(s.Domain))];
    }

    /// <summary>A plugin's server with what the configuration sets over it.</summary>
    private static McpServerOptions Over(McpServerOptions plugin, McpServerOptions configured) => new()
    {
        Domain = string.IsNullOrWhiteSpace(configured.Domain) ? plugin.Domain : configured.Domain,
        Endpoint = string.IsNullOrWhiteSpace(configured.Endpoint) ? plugin.Endpoint : configured.Endpoint,
        Tools = configured.Tools.Count > 0 ? configured.Tools : plugin.Tools,
    };
    public int HistoryTokenBudget { get; set; } = 3000;
    public int LongAnswerChars { get; set; } = 800;
    /// <summary>Issue the forced search_documents call on the model's behalf (Ollama ignores tool_choice).</summary>
    public bool EmulateRequiredToolMode { get; set; } = true;
    /// <summary>Request retrieval diagnostics from the MCP server for the behind-the-scenes monitor.</summary>
    public bool TraceRetrieval { get; set; } = true;
}

/// <summary>One domain's MCP server.</summary>
public sealed class McpServerOptions
{
    public string Domain { get; set; } = "";
    public string Endpoint { get; set; } = "";
    /// <summary>The server's tools the agent is offered; empty offers every tool it lists (add-codebase-domain).</summary>
    public List<string> Tools { get; set; } = [];
}
