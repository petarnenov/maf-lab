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
    [JsonPropertyName("private_to")] public string? PrivateTo { get; init; }
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
    [JsonPropertyName("corpus")] public CorpusTable? Corpus { get; init; }
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
    /// <summary>The domain question's key in the routing request; `in_{id}` when not set.</summary>
    [JsonPropertyName("question_key")] public string? QuestionKey { get; init; }
    /// <summary>What the domain covers, as the domain question reads it; the first routing question when not set.</summary>
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("order")] public int Order { get; init; } = 100;
    /// <summary>Read tools a data question may be routed to → their description for the routing question.</summary>
    [JsonPropertyName("read_tools")] public IReadOnlyDictionary<string, string> ReadTools { get; init; } = new Dictionary<string, string>();
    /// <summary>Write tools, asked about only as a veto → their description.</summary>
    [JsonPropertyName("write_tools")] public IReadOnlyDictionary<string, string> WriteTools { get; init; } = new Dictionary<string, string>();
    [JsonPropertyName("search_any_intent")] public bool SearchAnyIntent { get; init; }
    /// <summary>Tool → the plugin that must be in use for the tool to be offered.</summary>
    [JsonPropertyName("tool_requires")] public IReadOnlyDictionary<string, string> ToolRequires { get; init; } = new Dictionary<string, string>();
    /// <summary>The prompt fragment's file, relative to the plugin folder.</summary>
    [JsonPropertyName("prompt")] public string? Prompt { get; init; }
    /// <summary>The domain named for a user, by language, as the out-of-scope reply lists it.</summary>
    [JsonPropertyName("scope_summary")] public IReadOnlyDictionary<string, string> ScopeSummary { get; init; } = new Dictionary<string, string>();
    /// <summary>What the domain is about, as a short noun phrase Jev's contexts name ("fee billing").</summary>
    [JsonPropertyName("subject")] public string? Subject { get; init; }
    /// <summary>The domain's clauses in the intent options; a domain without them adds nothing.</summary>
    [JsonPropertyName("intent")] public DomainIntent? Intent { get; init; }

    /// <summary>The descriptor the core reads, with the prompt fragment's text when it was found.</summary>
    public DomainDescriptor ToDescriptor(string? promptFragment = null) =>
        new(Id, SearchTool, Tools, GraphTools, Routing, GuardContext ?? GuardContexts.Documents, CardTypes)
        {
            QuestionKey = string.IsNullOrWhiteSpace(QuestionKey) ? $"in_{Id}" : QuestionKey,
            Description = Description ?? (Routing.Count > 0 ? Routing[0] : null),
            Order = Order,
            ReadTools = ReadTools,
            WriteTools = WriteTools,
            SearchAnyIntent = SearchAnyIntent,
            ToolRequires = ToolRequires,
            PromptFragment = promptFragment,
            ScopeSummary = ScopeSummary,
            Subject = Subject,
            Intent = Intent,
        };
}

/// <summary>
/// A domain's clauses in the intent question's options (extract-billing): each a phrase the core joins after the
/// option's own generic stem, so the option says what in this domain is asked that way.
/// </summary>
public sealed record DomainIntent
{
    /// <summary>What in the domain a documentation question names ("what a named fee schedule … means or charges").</summary>
    [JsonPropertyName("procedural")] public string? Procedural { get; init; }
    /// <summary>The one record a how-or-why question about live data is about ("one specific billing run …").</summary>
    [JsonPropertyName("mixed")] public string? Mixed { get; init; }
    /// <summary>The live data a data question asks for, then its examples after a colon ("billing runs: a status, …").</summary>
    [JsonPropertyName("data")] public string? Data { get; init; }
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
    [JsonPropertyName("id")] public string? Id { get; init; }
    [JsonPropertyName("service")] public string? Service { get; init; }
    [JsonPropertyName("health")] public string? Health { get; init; }
    [JsonPropertyName("card")] public string? Card { get; init; }
    [JsonPropertyName("nodes")] public IReadOnlyList<TopologyTable> Nodes { get; init; } = [];
}

/// <summary>
/// The corpus a plugin owns (extract-index-admin-plugin): where it is in the plugin's folder, the collections it is
/// indexed into, how it is laid out and the graph it is built into, so the index admin can offer it.
/// </summary>
public sealed record CorpusTable
{
    /// <summary>Relative to the plugin's folder, never leaving it.</summary>
    [JsonPropertyName("path")] public string Path { get; init; } = "";
    [JsonPropertyName("collection")] public string Collection { get; init; } = "";
    /// <summary>Where the corpus's BM25 model lives, beside its collection.</summary>
    [JsonPropertyName("meta_collection")] public string MetaCollection { get; init; } = "";
    /// <summary><c>tenants</c> ({tenant}/... folders) or <c>repository</c> (the repository itself, every file shared).</summary>
    [JsonPropertyName("layout")] public string Layout { get; init; } = CorpusLayoutNames.Tenants;
    /// <summary>The graph source its documents are built into; none when the corpus has no graph.</summary>
    [JsonPropertyName("graph")] public string? Graph { get; init; }
}

/// <summary>The layouts a corpus can have, as the indexer reads them.</summary>
public static class CorpusLayoutNames
{
    public const string Tenants = "tenants";
    public const string Repository = "repository";
}

/// <summary>An installed plugin's corpus as the core resolved it: <see cref="Root"/> is absolute.</summary>
public sealed record PluginCorpus(string Plugin, string Root, string Collection, string MetaCollection, string Layout, string? Graph);
