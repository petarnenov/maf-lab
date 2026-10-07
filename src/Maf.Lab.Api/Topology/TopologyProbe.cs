using System.Diagnostics;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Maf.Lab.Api.Agent;
using Maf.Lab.Domain.Topology;
using Maf.Lab.Retrieval.Configuration;
using Maf.Lab.Hosting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Qdrant.Client;

namespace Maf.Lab.Api.Topology;

// names a domain until the topology follow-up moves it (introduce-plugins 8.1)
/// <summary>Resolves a compose service name to one address per replica; the balancer relies on the same fact.</summary>
public interface IServiceResolver
{
    Task<IReadOnlyList<string>> ResolveAsync(string service, CancellationToken ct);
}

public sealed class DnsServiceResolver : IServiceResolver
{
    public async Task<IReadOnlyList<string>> ResolveAsync(string service, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(service))
        {
            return [];
        }
        try
        {
            var addresses = await System.Net.Dns.GetHostAddressesAsync(service, ct);
            return [.. addresses.Select(a => a.ToString()).Distinct().Order()];
        }
        catch (SocketException)
        {
            // Not in the container network: the caller reports discovery as unavailable.
            return [];
        }
    }
}

/// <summary>
/// Asks every service of the stack the cheapest question that proves it works, all at once, each bounded by the
/// probe timeout, and never lets one failure fail the report. The result is reused for a few seconds so opening
/// the page repeatedly cannot turn into a probe storm.
/// </summary>
public sealed class TopologyProbe(
    IOptions<TopologyOptions> options,
    IOptions<QdrantOptions> qdrant,
    IOptions<ModelOptions> models,
    IOptions<AgentOptions> agent,
    IConfiguration configuration,
    IOptions<Coverage.TestAgentOptions> testAgent,
    IToolSource tools,
    QdrantClient qdrantClient,
    Maf.Lab.Retrieval.Graph.TenantScopedGraphMaintenance graph,
    IOptions<Maf.Lab.Retrieval.Graph.GraphOptions> graphOptions,
    IHttpClientFactory http,
    IMemoryCache cache,
    HealthCheckService health,
    TimeProvider time,
    IServiceResolver resolver,
    ILoggerFactory loggers,
    Plugins.PluginCatalogue? plugins = null,
    Maf.Lab.Plugins.Abstractions.IInstalledPlugins? installed = null,
    DomainCatalogue? domains = null)
{
    private const string CacheKey = "topology-report";

    /// <summary>Whether a plugin is in the installed set; with no set to read (a probe built by hand), everything is.</summary>
    private bool IsInstalled(string plugin) => installed is null || installed.IsInstalled(plugin);

    /// <summary>The infra plugin that runs the graph store, and the node that shows it.</summary>
    private const string GraphStorePlugin = "neo4j";

    /// <summary>The infra plugin that runs the vector store, and the node that shows it.</summary>
    private const string VectorStorePlugin = "qdrant";

    /// <summary>The plugin that runs the telemetry stack: the collector, the metrics store and the trace store.</summary>
    private const string ObservabilityPlugin = "observability";

    private static readonly TopologyEdge[] Edges =
    [
        new("lb", "web", "/"),
        new("lb", "api", "/api, /dev"),
        new("api", "mcp", "/mcp via lb"),
        new("api", "chat-provider", "chat"),
        new("lb", "compliance", "/compliance"),
        new("api", "compliance", "A2A via lb"),
        new("api", "qdrant", "search"),
        new("mcp", "qdrant", "gRPC"),
        new("mcp", "ollama-embeddings", "embed"),
        // The portfolio domain's own server: a turn that crosses domains goes from one MCP server to the other.
        new("api", "mcp-portfolio", "/portfolio/mcp via lb"),
        new("mcp-portfolio", "qdrant", "gRPC"),
        new("mcp-portfolio", "ollama-embeddings", "embed"),
        new("mcp-portfolio", "otel-collector", "OTLP"),
        // The codebase domain's server (add-codebase-domain): its own collection, and ask_codebase writes an answer.
        new("api", "mcp-code", "/code/mcp via lb"),
        new("mcp-code", "qdrant", "gRPC"),
        new("mcp-code", "ollama-embeddings", "embed"),
        new("mcp-code", "chat-provider", "ask_codebase"),
        new("mcp-code", "otel-collector", "OTLP"),
        // The graph store (add-neo4j-graph): billing relationships for the billing server, the code graph for mcp-code.
        new("mcp", "neo4j", "Bolt"),
        new("mcp-code", "neo4j", "Bolt"),
        // Where everything the lab emits about itself goes, and where it is kept.
        new("api", "otel-collector", "OTLP"),
        new("mcp", "otel-collector", "OTLP"),
        new("compliance", "otel-collector", "OTLP"),
        new("otel-collector", "prometheus", "metrics"),
        new("otel-collector", "jaeger", "traces"),
        new("api", "prometheus", "query"),
        new("lb", "jaeger", "/jaeger"),
        // The one place every replica reads: conversations' neighbours in flight, run state, tasks, webhooks.
        new("api", "redis", "shared state"),
        new("mcp", "redis", "idempotency"),
        new("compliance", "redis", "tasks"),
        // Test generation (add-coverage-dashboard-and-test-agent): the agent writes tests, the runner builds and measures
        // them — for the agent's attempts and, separately, for the api's own verification.
        new("api", "test-agent", "A2A"),
        new("test-agent", "coverage-runner", "run tests"),
        new("api", "coverage-runner", "verify, refresh"),
        new("test-agent", "chat-provider", "write tests"),
        new("test-agent", "redis", "tasks"),
        new("test-agent", "otel-collector", "OTLP"),
        new("coverage-runner", "otel-collector", "OTLP"),
    ];

    private readonly ILogger _logger = loggers.CreateLogger<TopologyProbe>();

    /// <summary>Node ids the report always contains; the drawn diagram must hold exactly these.</summary>
    public static IReadOnlyList<string> NodeIds { get; } =
    [
        "lb", "web", "api", "mcp", "mcp-portfolio", "mcp-code", "compliance", "test-agent", "coverage-runner", "qdrant", "neo4j",
        "ollama-embeddings", "chat-provider", "otel-collector", "prometheus", "jaeger", "redis",
    ];

    public async Task<TopologyReport> GetAsync(string bearerToken, CancellationToken ct)
    {
        if (cache.TryGetValue(CacheKey, out TopologyReport? cached) && cached is not null)
        {
            return cached;
        }
        var report = await ProbeAsync(bearerToken, ct);
        if (options.Value.CacheSeconds > 0)
        {
            cache.Set(CacheKey, report, TimeSpan.FromSeconds(options.Value.CacheSeconds));
        }
        return report;
    }

    private async Task<TopologyReport> ProbeAsync(string bearerToken, CancellationToken ct)
    {
        var o = options.Value;
        var timeout = TimeSpan.FromSeconds(o.ProbeTimeoutSeconds);
        var apiAddresses = await resolver.ResolveAsync(o.ApiService, ct);
        var mcpAddresses = await resolver.ResolveAsync(o.McpService, ct);
        var portfolioAddresses = await resolver.ResolveAsync(o.PortfolioService, ct);
        var codeAddresses = await resolver.ResolveAsync(o.CodeService, ct);
        var complianceAddresses = await resolver.ResolveAsync(o.ComplianceService, ct);
        var testAgentAddresses = await resolver.ResolveAsync(o.TestAgentService, ct);
        var runnerAddresses = await resolver.ResolveAsync(o.CoverageRunnerService, ct);
        var discovery = apiAddresses.Count > 0 || mcpAddresses.Count > 0 || portfolioAddresses.Count > 0 || codeAddresses.Count > 0;

        var lb = Http("lb", "lb", o.LoadBalancerHealthUrl, timeout, ct);
        var web = Http("web", "web", o.WebHealthUrl, timeout, ct);
        var api = ReplicasAsync("api", "api", apiAddresses, timeout, ct);
        // One tools/list per server for the whole report: each domain's node reads its own tools from the same answer.
        var offered = OfferedAsync(bearerToken, timeout, ct);
        var mcp = DomainServerAsync("mcp", BuiltIn.BuiltInDomains.Billing, mcpAddresses, offered, timeout, ct, "mcp-retrieval");
        var portfolio = DomainServerAsync("mcp-portfolio", BuiltIn.BuiltInDomains.Portfolio, portfolioAddresses, offered, timeout, ct);
        var code = DomainServerAsync("mcp-code", "codebase", codeAddresses, offered, timeout, ct);
        var compliance = ComplianceAsync(complianceAddresses, timeout, ct);
        var agentNode = TestAgentAsync(testAgentAddresses, timeout, ct);
        var runnerNode = ReplicasAsync("coverage-runner", "coverage runner", runnerAddresses, timeout, ct);
        var store = QdrantAsync(timeout, ct);
        var graph = GraphAsync(timeout, ct);
        var embeddings = EmbeddingsAsync(timeout, ct);
        // The telemetry stack is a plugin (extract-observability): without it there is nothing to probe, and that is not a fault.
        var stack = IsInstalled(ObservabilityPlugin);
        Task<TopologyNode> Telemetry(string id, string name, string url) => stack
            ? Http(id, name, url, timeout, ct)
            : Task.FromResult(new TopologyNode(id, name, NodeHealth.NotProbed, [],
                new Dictionary<string, string> { ["url"] = url, ["endpoint"] = "not installed" },
                "the telemetry stack is not installed"));
        var collector = Telemetry("otel-collector", "otel collector", o.CollectorHealthUrl);
        var metrics = Telemetry("prometheus", "prometheus", o.PrometheusHealthUrl);
        var traces = Telemetry("jaeger", "jaeger", o.JaegerHealthUrl);
        var shared = SharedStateAsync(ct);

        // Each installed plugin that names a topology address (introduce-plugins decision 3) is probed like any other
        // service and listed after the core's nodes; the drawn diagram holds only the core's.
        var pluginNodes = (installed?.Installed() ?? [])
            .Where(m => m.Topology?.Url is { Length: > 0 } && !NodeIds.Contains(m.Name))
            .Select(m => Http(m.Name, m.Topology!.Label ?? m.Name, m.Topology.Url, timeout, ct))
            .ToList();

        var probed = await Task.WhenAll(lb, web, api, mcp, portfolio, code, compliance, agentNode, runnerNode, store, graph, embeddings, collector,
            metrics, traces, shared);
        var byId = probed.Append(ChatProvider()).ToDictionary(n => n.Id);
        var ordered = NodeIds.Select(id => byId[id]).Concat(await Task.WhenAll(pluginNodes)).ToList();

        return new TopologyReport(time.GetUtcNow(), o.CacheSeconds, discovery, InstanceIdentity.Name, ordered, Edges);
    }

    /// <summary>
    /// The shared store, asked the way the api asks it: this replica's own connection. A store that answers here
    /// is a store this replica can serve from, which is the only question the report is about.
    /// </summary>
    private async Task<TopologyNode> SharedStateAsync(CancellationToken ct)
    {
        var facts = new Dictionary<string, string> { ["role"] = "shared state" };
        // The shared state's health check (ASP.NET Core Health Checks), by its tag: the same answer /health gives.
        var report = await health.CheckHealthAsync(c => c.Tags.Contains(Maf.Lab.Hosting.SharedStateHealth.Tag), ct);
        var entry = report.Entries.Values.FirstOrDefault();
        return report.Status == HealthStatus.Healthy
            ? new TopologyNode("redis", "redis", NodeHealth.Healthy, [], facts, null)
            : new TopologyNode("redis", "redis", NodeHealth.Unreachable, [], facts, entry.Description);
    }

    /// <summary>The paid remote chat endpoint is deliberately not contacted; configuration is the whole answer.</summary>
    private TopologyNode ChatProvider()
    {
        var m = models.Value;
        var endpoint = string.IsNullOrWhiteSpace(m.ChatEndpoint) ? m.OllamaEndpoint : m.ChatEndpoint;
        var uri = Uri.TryCreate(endpoint, UriKind.Absolute, out var parsed) ? parsed : null;
        var remote = uri is not null && !uri.IsLoopback && uri.Host is not ("ollama" or "host.docker.internal");
        var hasKey = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(m.ChatApiKeyEnvironmentVariable));
        var facts = new Dictionary<string, string>
        {
            ["provider"] = m.Provider,
            ["endpoint"] = uri?.GetLeftPart(UriPartial.Authority) ?? endpoint,
            ["chatModel"] = m.ChatModel,
            ["remote"] = remote ? "yes" : "no",
            // Whether a key is configured — never the key itself.
            ["apiKey"] = hasKey ? $"set ({m.ChatApiKeyEnvironmentVariable})" : "not set",
        };
        return remote && !hasKey
            ? new TopologyNode("chat-provider", "chat provider", NodeHealth.Degraded, [], facts,
                $"{m.ChatApiKeyEnvironmentVariable} is not set; chat will fail")
            : new TopologyNode("chat-provider", "chat provider", NodeHealth.NotProbed, [], facts,
                "not probed: a paid remote endpoint is not pinged to refresh a page");
    }

    private async Task<TopologyNode> ReplicasAsync(string id, string name, IReadOnlyList<string> addresses, TimeSpan timeout, CancellationToken ct)
    {
        if (addresses.Count == 0)
        {
            // Outside the container network this host can still speak for itself.
            return new TopologyNode(id, name, NodeHealth.Healthy,
                [new TopologyInstance(InstanceIdentity.Name, null, NodeHealth.Healthy)],
                new Dictionary<string, string> { ["replicas"] = "1 (discovery unavailable)" },
                "replicas could not be discovered; reporting this instance only");
        }
        var instances = await Task.WhenAll(addresses.Select(a => HealthOfAsync(a, timeout, ct)));
        return Aggregate(id, name, instances);
    }

    /// <summary>
    /// The second agent: its replicas, and whether it is still the agent we think it is — the card says both.
    /// </summary>
    private async Task<TopologyNode> ComplianceAsync(IReadOnlyList<string> addresses, TimeSpan timeout, CancellationToken ct)
    {
        var node = await ReplicasAsync("compliance", "compliance", addresses, timeout, ct);
        // The reviewer's address as the compliance plugin is configured (its own options type is the plugin's).
        var baseUrl = configuration["Compliance:BaseUrl"] ?? "";
        var facts = new Dictionary<string, string>(node.Facts) { ["baseUrl"] = baseUrl };
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return node with { Facts = facts, Health = NodeHealth.Degraded, Reason = "no compliance agent is configured" };
        }
        try
        {
            using var cts = Linked(timeout, ct);
            var client = http.CreateClient("topology");
            var card = await client.GetFromJsonAsync<System.Text.Json.JsonElement>(
                $"{baseUrl.TrimEnd('/')}{Maf.Lab.A2A.AgentCardFactory.WellKnownPath}", cts.Token);
            facts["agent"] = card.TryGetProperty("name", out var name) ? name.GetString() ?? "?" : "?";
            facts["skills"] = string.Join(", ", card.GetProperty("skills").EnumerateArray()
                .Select(s => s.GetProperty("id").GetString()));
        }
        catch (Exception ex)
        {
            return node with
            {
                Facts = facts,
                Health = node.Instances.Any(i => i.Health == NodeHealth.Healthy) ? NodeHealth.Degraded : node.Health,
                Reason = Describe(ex, timeout),
            };
        }
        return node with { Facts = facts };
    }

    /// <summary>The test agent: its replicas, and what its card says it offers. Unconfigured is degraded, not down.</summary>
    private async Task<TopologyNode> TestAgentAsync(IReadOnlyList<string> addresses, TimeSpan timeout, CancellationToken ct)
    {
        var node = await ReplicasAsync("test-agent", "test agent", addresses, timeout, ct);
        var baseUrl = testAgent.Value.BaseUrl;
        var facts = new Dictionary<string, string>(node.Facts) { ["baseUrl"] = baseUrl };
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return node with { Facts = facts, Health = NodeHealth.Degraded, Reason = "no test agent is configured" };
        }
        try
        {
            using var cts = Linked(timeout, ct);
            var card = await http.CreateClient("topology").GetFromJsonAsync<System.Text.Json.JsonElement>(
                $"{baseUrl.TrimEnd('/')}{Maf.Lab.A2A.AgentCardFactory.WellKnownPath}", cts.Token);
            facts["agent"] = card.TryGetProperty("name", out var name) ? name.GetString() ?? "?" : "?";
            facts["skills"] = string.Join(", ", card.GetProperty("skills").EnumerateArray().Select(s => s.GetProperty("id").GetString()));
        }
        catch (Exception ex)
        {
            return node with
            {
                Facts = facts,
                Health = node.Instances.Any(i => i.Health == NodeHealth.Healthy) ? NodeHealth.Degraded : node.Health,
                Reason = Describe(ex, timeout),
            };
        }
        return node with { Facts = facts };
    }

    /// <summary>What the turn's tool source offers, by domain, or why it could not be read.</summary>
    private sealed record Offered(IReadOnlyDictionary<string, string[]> ByDomain, IReadOnlyList<string> Unavailable, string? Error);

    private async Task<Offered> OfferedAsync(string bearerToken, TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            using var cts = Linked(timeout, ct);
            await using var set = await tools.GetToolsAsync(bearerToken, null, cts.Token);
            return new Offered(set.Names.GroupBy(set.DomainOf).ToDictionary(g => g.Key, g => g.Order().ToArray()), set.Unavailable, null);
        }
        catch (Exception ex)
        {
            return new Offered(new Dictionary<string, string[]>(), [], Describe(ex, timeout));
        }
    }

    /// <summary>
    /// A domain's MCP server (billing, portfolio, codebase), as the agent sees it — configured through Agent:Servers or
    /// named by an installed plugin's server.json — every one alike, so an unconfigured billing server degrades like any
    /// other (introduce-plugins 4.6). A domain that is not installed is reported as such, not as a fault.
    /// </summary>
    private async Task<TopologyNode> DomainServerAsync(string id, string domain, IReadOnlyList<string> addresses, Task<Offered> offered,
        TimeSpan timeout, CancellationToken ct, string? name = null)
    {
        var endpoint = agent.Value.AllServers(plugins?.McpServers()).FirstOrDefault(s => s.Domain == domain)?.Endpoint;
        var node = await ReplicasAsync(id, name ?? id, addresses, timeout, ct);
        if ((domains ?? DomainCatalogue.Empty).Get(domain) is null)
        {
            return node with
            {
                Facts = new Dictionary<string, string>(node.Facts) { ["endpoint"] = "not installed" },
                Health = NodeHealth.NotProbed,
                Reason = $"the {domain} domain is not installed",
            };
        }
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return node with
            {
                Facts = new Dictionary<string, string>(node.Facts) { ["endpoint"] = "not configured" },
                Health = NodeHealth.Degraded,
                Reason = $"no {domain} MCP server is configured (Agent:Servers)",
            };
        }
        var o = await offered;
        // Its own tools/list failing shows up as the domain being unavailable, not as the whole call failing.
        return o.Unavailable.Contains(domain)
            ? WithTools(node, o with { Error = "tools/list failed" }, domain, endpoint)
            : WithTools(node, o, domain, endpoint);
    }

    /// <summary>The domain's tools on its node; a failed tools/list degrades it, or makes it unreachable if no replica answers.</summary>
    private static TopologyNode WithTools(TopologyNode node, Offered offered, string domain, string endpoint)
    {
        var names = offered.ByDomain.GetValueOrDefault(domain) ?? [];
        var facts = new Dictionary<string, string>(node.Facts)
        {
            ["endpoint"] = endpoint,
            ["domain"] = domain,
            ["tools"] = names.Length > 0 ? $"{names.Length}: {string.Join(", ", names)}" : "unknown",
        };
        // A failed tools/list means the path through the balancer is not working; that is the whole service only
        // when no replica answers at all, otherwise it is degraded (a replica just went away, say).
        var anyReplicaHealthy = node.Instances.Any(i => i.Health == NodeHealth.Healthy);
        var health = offered.Error is null ? node.Health
            : anyReplicaHealthy ? NodeHealth.Degraded
            : NodeHealth.Unreachable;
        var reason = offered.Error is not null ? $"tools/list failed: {offered.Error}" : node.Reason;
        return node with { Health = health, Facts = facts, Reason = reason };
    }

    private async Task<TopologyNode> QdrantAsync(TimeSpan timeout, CancellationToken ct)
    {
        var facts = new Dictionary<string, string> { ["collection"] = qdrant.Value.Collection, ["host"] = $"{qdrant.Value.Host}:{qdrant.Value.GrpcPort}" };
        // The vector store is a plugin (extract-portfolio): without it there is nothing to probe, and that is not a fault.
        if (!IsInstalled(VectorStorePlugin))
        {
            facts["endpoint"] = "not installed";
            return new TopologyNode("qdrant", "qdrant", NodeHealth.NotProbed, [], facts, "the vector store is not installed");
        }
        try
        {
            using var cts = Linked(timeout, ct);
            var collections = await qdrantClient.ListCollectionsAsync(cts.Token);
            if (!collections.Contains(qdrant.Value.Collection))
            {
                facts["chunks"] = "0";
                return new TopologyNode("qdrant", "qdrant", NodeHealth.Degraded, [], facts,
                    $"collection {qdrant.Value.Collection} does not exist; index the corpus");
            }
            var info = await qdrantClient.GetCollectionInfoAsync(qdrant.Value.Collection, cts.Token);
            facts["chunks"] = info.PointsCount.ToString();
            facts["status"] = info.Status.ToString();
            return new TopologyNode("qdrant", "qdrant", NodeHealth.Healthy, [], facts, null);
        }
        catch (Exception ex)
        {
            return new TopologyNode("qdrant", "qdrant", NodeHealth.Unreachable, [], facts, Describe(ex, timeout));
        }
    }

    /// <summary>
    /// The graph store, asked only whether it answers: the api reads no graph data, and a count would be a query outside
    /// the one tenant-scoped read path. Its address and database are facts; its credentials never are.
    /// </summary>
    private async Task<TopologyNode> GraphAsync(TimeSpan timeout, CancellationToken ct)
    {
        var o = graphOptions.Value;
        var facts = new Dictionary<string, string> { ["role"] = "graph store", ["host"] = o.Authority, ["database"] = o.Database };
        // The graph store is a plugin (extract-billing): without it there is nothing to probe, and that is not a fault.
        if (!IsInstalled(GraphStorePlugin))
        {
            facts["endpoint"] = "not installed";
            return new TopologyNode("neo4j", "neo4j", NodeHealth.NotProbed, [], facts, "the graph store is not installed");
        }
        try
        {
            using var cts = Linked(timeout, ct);
            await graph.VerifyConnectivityAsync(cts.Token);
            return new TopologyNode("neo4j", "neo4j", NodeHealth.Healthy, [], facts, null);
        }
        catch (Exception ex)
        {
            return new TopologyNode("neo4j", "neo4j", NodeHealth.Unreachable, [], facts,
                ex is Neo4j.Driver.AuthenticationException ? "credentials refused" : Describe(ex, timeout));
        }
    }

    /// <summary>
    /// The embedding provider: one service with an instance per role — <c>interactive</c> (search queries) and, when
    /// configured, <c>batch</c> (documents). Each is asked for its pulled models; one that answers without the model is
    /// degraded, and the service is unreachable only when no instance answers.
    /// </summary>
    private async Task<TopologyNode> EmbeddingsAsync(TimeSpan timeout, CancellationToken ct)
    {
        const string id = "ollama-embeddings", name = "ollama (embeddings)";
        var wanted = models.Value.Embeddings.Values.Select(e => e.Model).Where(m => !string.IsNullOrWhiteSpace(m)).Distinct().ToList();
        var facts = new Dictionary<string, string>
        {
            ["endpoint"] = models.Value.OllamaEndpoint,
            ["models"] = string.Join(", ", wanted),
        };
        var roles = new List<(string Role, string Endpoint)> { ("interactive", models.Value.OllamaEndpoint) };
        if (!string.IsNullOrWhiteSpace(models.Value.BatchOllamaEndpoint))
        {
            facts["batchEndpoint"] = models.Value.BatchOllamaEndpoint;
            roles.Add(("batch", models.Value.BatchOllamaEndpoint));
        }
        var probes = await Task.WhenAll(roles.Select(r => EmbeddingInstanceAsync(r.Role, r.Endpoint, wanted, timeout, ct)));
        var instances = probes.Select(p => p.Instance).ToList();
        facts["pulled"] = string.Join(", ", probes.Select(p => $"{p.Instance.Name}: {p.Pulled?.ToString() ?? "?"}"));

        var answering = probes.Count(p => p.Pulled is not null);
        var health = answering == 0 ? NodeHealth.Unreachable
            : instances.All(i => i.Health == NodeHealth.Healthy) ? NodeHealth.Healthy
            : NodeHealth.Degraded;
        var reason = health == NodeHealth.Healthy
            ? null
            : roles.Count == 1
                ? instances[0].Reason
                : string.Join("; ", instances.Where(i => i.Health != NodeHealth.Healthy).Select(i => $"{i.Name}: {i.Reason}"));
        return new TopologyNode(id, name, health, instances, facts, reason);
    }

    /// <summary>One embedding instance: healthy with every wanted model pulled, degraded without one, unreachable silent.</summary>
    private async Task<(TopologyInstance Instance, int? Pulled)> EmbeddingInstanceAsync(
        string role, string endpoint, IReadOnlyList<string> wanted, TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            using var cts = Linked(timeout, ct);
            using var response = await Client().GetAsync($"{endpoint.TrimEnd('/')}/api/tags", cts.Token);
            response.EnsureSuccessStatusCode();
            var pulled = (await JsonSerializer.DeserializeAsync<JsonElement>(await response.Content.ReadAsStreamAsync(cts.Token), cancellationToken: cts.Token))
                .TryGetProperty("models", out var list) && list.ValueKind == JsonValueKind.Array
                    ? list.EnumerateArray().Select(m => m.TryGetProperty("model", out var n) ? n.GetString() ?? "" : "").ToList()
                    : [];
            // Ollama reports "embeddinggemma:latest" for "embeddinggemma".
            var missing = wanted.Where(w => !pulled.Any(p => p == w || p.StartsWith(w + ":", StringComparison.Ordinal))).ToList();
            return missing.Count > 0
                ? (new TopologyInstance(role, endpoint, NodeHealth.Degraded, $"not pulled: {string.Join(", ", missing)}"), pulled.Count)
                : (new TopologyInstance(role, endpoint, NodeHealth.Healthy), pulled.Count);
        }
        catch (Exception ex)
        {
            return (new TopologyInstance(role, endpoint, NodeHealth.Unreachable, Describe(ex, timeout)), null);
        }
    }

    private async Task<TopologyNode> Http(string id, string name, string url, TimeSpan timeout, CancellationToken ct)
    {
        var facts = new Dictionary<string, string> { ["url"] = url };
        try
        {
            using var cts = Linked(timeout, ct);
            using var response = await Client().GetAsync(url, cts.Token);
            response.EnsureSuccessStatusCode();
            return new TopologyNode(id, name, NodeHealth.Healthy, [], facts, null);
        }
        catch (Exception ex)
        {
            return new TopologyNode(id, name, NodeHealth.Unreachable, [], facts, Describe(ex, timeout));
        }
    }

    /// <summary>Asks one replica its own /health, which answers with the container's instance name.</summary>
    private async Task<TopologyInstance> HealthOfAsync(string address, TimeSpan timeout, CancellationToken ct)
    {
        var host = address.Contains(':') ? $"[{address}]" : address;
        try
        {
            using var cts = Linked(timeout, ct);
            using var response = await Client().GetAsync($"http://{host}:{options.Value.ServicePort}/health", cts.Token);
            response.EnsureSuccessStatusCode();
            var body = await JsonSerializer.DeserializeAsync<JsonElement>(await response.Content.ReadAsStreamAsync(cts.Token), cancellationToken: cts.Token);
            var name = body.TryGetProperty("instance", out var instance) ? instance.GetString() : null;
            return new TopologyInstance(name ?? address, address, NodeHealth.Healthy);
        }
        catch (Exception ex)
        {
            _logger.LogDebug("topology: {Address} did not answer /health ({Error})", address, ex.GetType().Name);
            return new TopologyInstance(address, address, NodeHealth.Unreachable, Describe(ex, timeout));
        }
    }

    private static TopologyNode Aggregate(string id, string name, IReadOnlyList<TopologyInstance> instances)
    {
        var healthy = instances.Count(i => i.Health == NodeHealth.Healthy);
        var health = healthy == instances.Count ? NodeHealth.Healthy
            : healthy == 0 ? NodeHealth.Unreachable
            : NodeHealth.Degraded;
        // The count is what a stopped replica shows up as: Docker DNS stops resolving it, so it cannot be listed.
        var facts = new Dictionary<string, string>
        {
            ["replicas"] = $"{healthy}/{instances.Count} healthy",
            ["discovered"] = $"{instances.Count} address(es)",
        };
        var reason = health == NodeHealth.Healthy
            ? null
            : $"silent: {string.Join(", ", instances.Where(i => i.Health != NodeHealth.Healthy).Select(i => i.Name))}";
        return new TopologyNode(id, name, health, instances, facts, reason);
    }

    private HttpClient Client() => http.CreateClient("topology");

    private CancellationTokenSource Linked(TimeSpan timeout, CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        return cts;
    }

    private static string Describe(Exception ex, TimeSpan timeout) =>
        ex is OperationCanceledException or TaskCanceledException
            ? $"no answer within {timeout.TotalSeconds:0.#}s"
            : $"{ex.GetType().Name}: {ex.Message.Split('\n')[0]}";
}
