using System.Diagnostics;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Maf.Lab.Api.Agent;
using Maf.Lab.Domain.Topology;
using Maf.Lab.Retrieval.Configuration;
using Maf.Lab.Hosting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Qdrant.Client;

namespace Maf.Lab.Api.Topology;

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
    IOptions<A2A.ComplianceOptions> compliance,
    IToolSource tools,
    QdrantClient qdrantClient,
    IHttpClientFactory http,
    IMemoryCache cache,
    Maf.Lab.Hosting.SharedStateHealth shared,
    TimeProvider time,
    IServiceResolver resolver,
    ILoggerFactory loggers)
{
    private const string CacheKey = "topology-report";

    private static readonly TopologyEdge[] Edges =
    [
        new("lb", "web", "/"),
        new("lb", "api", "/api, /dev"),
        new("api", "mcp", "/mcp via lb"),
        new("api", "chat-provider", "chat"),
        new("lb", "compliance", "/compliance"),
        new("api", "compliance", "A2A via lb"),
        new("api", "qdrant", "index admin"),
        new("mcp", "qdrant", "gRPC"),
        new("mcp", "ollama-embeddings", "embed"),
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
    ];

    private readonly ILogger _logger = loggers.CreateLogger<TopologyProbe>();

    /// <summary>Node ids the report always contains; the drawn diagram must hold exactly these.</summary>
    public static IReadOnlyList<string> NodeIds { get; } =
    [
        "lb", "web", "api", "mcp", "compliance", "qdrant", "ollama-embeddings", "chat-provider",
        "otel-collector", "prometheus", "jaeger", "redis",
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
        var complianceAddresses = await resolver.ResolveAsync(o.ComplianceService, ct);
        var discovery = apiAddresses.Count > 0 || mcpAddresses.Count > 0;

        var lb = Http("lb", "lb", o.LoadBalancerHealthUrl, timeout, ct);
        var web = Http("web", "web", o.WebHealthUrl, timeout, ct);
        var api = ReplicasAsync("api", "api", apiAddresses, timeout, ct);
        var mcp = McpAsync(mcpAddresses, bearerToken, timeout, ct);
        var compliance = ComplianceAsync(complianceAddresses, timeout, ct);
        var store = QdrantAsync(timeout, ct);
        var embeddings = EmbeddingsAsync(timeout, ct);
        var collector = Http("otel-collector", "otel collector", o.CollectorHealthUrl, timeout, ct);
        var metrics = Http("prometheus", "prometheus", o.PrometheusHealthUrl, timeout, ct);
        var traces = Http("jaeger", "jaeger", o.JaegerHealthUrl, timeout, ct);
        var shared = SharedStateAsync(ct);

        var probed = await Task.WhenAll(lb, web, api, mcp, compliance, store, embeddings, collector, metrics, traces, shared);
        var byId = probed.Append(ChatProvider()).ToDictionary(n => n.Id);
        var ordered = NodeIds.Select(id => byId[id]).ToList();

        return new TopologyReport(time.GetUtcNow(), o.CacheSeconds, discovery, InstanceIdentity.Name, ordered, Edges);
    }

    /// <summary>
    /// The shared store, asked the way the api asks it: this replica's own connection. A store that answers here
    /// is a store this replica can serve from, which is the only question the report is about.
    /// </summary>
    private async Task<TopologyNode> SharedStateAsync(CancellationToken ct)
    {
        var facts = new Dictionary<string, string> { ["role"] = "shared state" };
        var (ok, reason) = await shared.CheckAsync(ct);
        return ok
            ? new TopologyNode("redis", "redis", NodeHealth.Healthy, [], facts, null)
            : new TopologyNode("redis", "redis", NodeHealth.Unreachable, [], facts, reason);
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
        var facts = new Dictionary<string, string>(node.Facts) { ["baseUrl"] = compliance.Value.BaseUrl };
        if (string.IsNullOrWhiteSpace(compliance.Value.BaseUrl))
        {
            return node with { Facts = facts, Health = NodeHealth.Degraded, Reason = "no compliance agent is configured" };
        }
        try
        {
            using var cts = Linked(timeout, ct);
            var client = http.CreateClient("topology");
            var card = await client.GetFromJsonAsync<System.Text.Json.JsonElement>(
                $"{compliance.Value.BaseUrl.TrimEnd('/')}{Maf.Lab.A2A.AgentCardFactory.WellKnownPath}", cts.Token);
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

    private async Task<TopologyNode> McpAsync(IReadOnlyList<string> addresses, string bearerToken, TimeSpan timeout, CancellationToken ct)
    {
        var replicas = ReplicasAsync("mcp", "mcp-retrieval", addresses, timeout, ct);
        var toolNames = Array.Empty<string>();
        string? toolError = null;
        try
        {
            using var cts = Linked(timeout, ct);
            await using var set = await tools.GetToolsAsync(bearerToken, null, cts.Token);
            toolNames = [.. set.Names.Order()];
        }
        catch (Exception ex)
        {
            toolError = Describe(ex, timeout);
        }
        var node = await replicas;
        var facts = new Dictionary<string, string>(node.Facts)
        {
            ["endpoint"] = agent.Value.McpEndpoint,
            ["tools"] = toolNames.Length > 0 ? $"{toolNames.Length}: {string.Join(", ", toolNames)}" : "unknown",
        };
        // A failed tools/list means the path through the balancer is not working; that is the whole service only
        // when no replica answers at all, otherwise it is degraded (a replica just went away, say).
        var anyReplicaHealthy = node.Instances.Any(i => i.Health == NodeHealth.Healthy);
        var health = toolError is null ? node.Health
            : anyReplicaHealthy ? NodeHealth.Degraded
            : NodeHealth.Unreachable;
        var reason = toolError is not null ? $"tools/list failed: {toolError}" : node.Reason;
        return node with { Health = health, Facts = facts, Reason = reason };
    }

    private async Task<TopologyNode> QdrantAsync(TimeSpan timeout, CancellationToken ct)
    {
        var facts = new Dictionary<string, string> { ["collection"] = qdrant.Value.Collection, ["host"] = $"{qdrant.Value.Host}:{qdrant.Value.GrpcPort}" };
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

    private async Task<TopologyNode> EmbeddingsAsync(TimeSpan timeout, CancellationToken ct)
    {
        var wanted = models.Value.Embeddings.Values.Select(e => e.Model).Where(m => !string.IsNullOrWhiteSpace(m)).Distinct().ToList();
        var facts = new Dictionary<string, string>
        {
            ["endpoint"] = models.Value.OllamaEndpoint,
            ["models"] = string.Join(", ", wanted),
        };
        try
        {
            using var cts = Linked(timeout, ct);
            using var response = await Client().GetAsync($"{models.Value.OllamaEndpoint.TrimEnd('/')}/api/tags", cts.Token);
            response.EnsureSuccessStatusCode();
            var pulled = (await JsonSerializer.DeserializeAsync<JsonElement>(await response.Content.ReadAsStreamAsync(cts.Token), cancellationToken: cts.Token))
                .TryGetProperty("models", out var list) && list.ValueKind == JsonValueKind.Array
                    ? list.EnumerateArray().Select(m => m.TryGetProperty("model", out var n) ? n.GetString() ?? "" : "").ToList()
                    : [];
            // Ollama reports "nomic-embed-text:latest" for "nomic-embed-text".
            var missing = wanted.Where(w => !pulled.Any(p => p == w || p.StartsWith(w + ":", StringComparison.Ordinal))).ToList();
            facts["pulled"] = pulled.Count.ToString();
            return missing.Count > 0
                ? new TopologyNode("ollama-embeddings", "ollama (embeddings)", NodeHealth.Degraded, [], facts,
                    $"not pulled: {string.Join(", ", missing)}")
                : new TopologyNode("ollama-embeddings", "ollama (embeddings)", NodeHealth.Healthy, [], facts, null);
        }
        catch (Exception ex)
        {
            return new TopologyNode("ollama-embeddings", "ollama (embeddings)", NodeHealth.Unreachable, [], facts,
                Describe(ex, timeout));
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
