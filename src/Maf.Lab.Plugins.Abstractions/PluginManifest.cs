using System.Text.Json.Serialization;

namespace Maf.Lab.Plugins.Abstractions;

/// <summary>The kinds a plugin can be (introduce-plugins decision 1).</summary>
public static class PluginKinds
{
    public const string Mcp = "mcp";
    public const string A2A = "a2a";
    public const string App = "app";
    public const string Provider = "provider";
    public const string Infra = "infra";
}

/// <summary>Who a plugin is for: a tenant's choice, or the whole installation.</summary>
public static class PluginScopes
{
    public const string Tenant = "tenant";
    public const string Installation = "installation";
}

/// <summary>
/// A plugin's manifest, as `plugins/.installed` carries it (make converts each `plugin.toml`, validated against
/// `plugins/plugin.schema.json`, so the core never parses TOML). Only what no established format holds lives here: an MCP
/// plugin's server is its MCP Registry `server.json`, an A2A plugin's agent is its Agent Card.
/// </summary>
public sealed record PluginManifest
{
    [JsonPropertyName("schema")] public int Schema { get; init; } = 1;
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("kind")] public string Kind { get; init; } = "";
    [JsonPropertyName("scope")] public string Scope { get; init; } = PluginScopes.Installation;
    [JsonPropertyName("environments")] public IReadOnlyList<string> Environments { get; init; } = [];
    [JsonPropertyName("description")] public string Description { get; init; } = "";
    [JsonPropertyName("depends")] public IReadOnlyList<string> Depends { get; init; } = [];
    [JsonPropertyName("provides")] public string? Provides { get; init; }
    /// <summary>Listed by `/api/plugins` before sign-in. Only a sign-in plugin (dev-login) needs it.</summary>
    [JsonPropertyName("public")] public bool Public { get; init; }
    [JsonPropertyName("progress")] public string Progress { get; init; } = "";
    [JsonPropertyName("stopping")] public string Stopping { get; init; } = "";
    [JsonPropertyName("domain")] public DomainTable? Domain { get; init; }
    [JsonPropertyName("agent")] public AgentTable? Agent { get; init; }
    [JsonPropertyName("topology")] public TopologyTable? Topology { get; init; }
}

/// <summary>How the core uses an MCP plugin's tools: data, operator-reviewed, never read from the server (decision 5a).</summary>
public sealed record DomainTable
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    /// <summary>The allow-list of the server's tools; the endpoint and the full list come from server.json and tools/list.</summary>
    [JsonPropertyName("tools")] public IReadOnlyList<string> Tools { get; init; } = [];
    [JsonPropertyName("search_tool")] public string? SearchTool { get; init; }
    [JsonPropertyName("graph_tools")] public IReadOnlyList<string> GraphTools { get; init; } = [];
    [JsonPropertyName("guard_context")] public string? GuardContext { get; init; }
    [JsonPropertyName("routing")] public IReadOnlyList<string> Routing { get; init; } = [];
    /// <summary>Tool name → AG-UI activity type of the card its result travels as.</summary>
    [JsonPropertyName("card_types")] public IReadOnlyDictionary<string, string> CardTypes { get; init; } = new Dictionary<string, string>();
}

/// <summary>An AG-UI agent a plugin serves: the runtime maps its name to this path (decision 3).</summary>
public sealed record AgentTable
{
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("path")] public string Path { get; init; } = "";
}

/// <summary>Where the topology probe reaches a plugin's service, and what it is called there.</summary>
public sealed record TopologyTable
{
    [JsonPropertyName("url")] public string Url { get; init; } = "";
    [JsonPropertyName("label")] public string? Label { get; init; }
}
