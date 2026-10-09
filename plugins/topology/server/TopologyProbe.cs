using System.Net.Http.Json;
using Maf.Lab.Domain.Services;
using System.Text.Json;
using Maf.Lab.Domain.Topology;
using Maf.Lab.Plugins.Abstractions;
using Maf.Lab.Retrieval.Configuration;
using Maf.Lab.Retrieval.Graph;
using Maf.Lab.Retrieval.Store;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;

namespace Maf.Lab.Plugins.Topology;

/// <summary>Bounded probes over installed manifest data and shared store adapters.</summary>
public sealed class TopologyProbe(IOptions<TopologyOptions> options, IOptions<QdrantOptions> qdrant,
    IOptions<ModelOptions> models, CollectionBootstrapper vectors, TenantScopedGraphMaintenance graph,
    IOptions<GraphOptions> graphOptions, IHttpClientFactory http, IMemoryCache cache, HealthCheckService health,
    TimeProvider time, IServiceResolver resolver, ILoggerFactory loggers, IInstalledPlugins installed, IPluginTokens? tokens = null)
{
    public static IReadOnlyList<string> CoreNodeIds { get; } = ["lb", "web", "api", "redis", "ollama-embeddings", "chat-provider"];
    private readonly ILogger _logger = loggers.CreateLogger<TopologyProbe>();

    public async Task<TopologyReport> GetAsync(string bearerToken, CancellationToken ct)
    {
        // tools/list runs as the caller: cached names must never be borrowed from another principal.
        var key = "topology-report:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(bearerToken)));
        if (cache.TryGetValue(key, out TopologyReport? previous) && previous is not null) return previous;
        var report = await ProbeAsync(bearerToken, ct);
        if (options.Value.CacheSeconds > 0) cache.Set(key, report, TimeSpan.FromSeconds(options.Value.CacheSeconds));
        return report;
    }

    internal static IEnumerable<(PluginManifest Manifest, TopologyTable Node)> DeclaredNodes(IEnumerable<PluginManifest> manifests) =>
        manifests.Where(m => m.Topology is not null).SelectMany(m =>
            (HasProbe(m.Topology!) ? new[] { m.Topology! } : []).Concat(m.Topology!.Nodes).Select(n => (m, n)));
    private static bool HasProbe(TopologyTable table) => !string.IsNullOrWhiteSpace(table.Service) || !string.IsNullOrWhiteSpace(table.Url);
    private static string NodeId(PluginManifest manifest, TopologyTable node) => node.Id ?? node.Service ?? manifest.Name;

    private async Task<TopologyReport> ProbeAsync(string bearerToken, CancellationToken ct)
    {
        var timeout = TimeSpan.FromSeconds(options.Value.ProbeTimeoutSeconds);
        var shared = SharedAsync(timeout, ct);
        async Task<TopologyNode> SharedNode() => (await shared).Node;
        var core = new[] {
            Http("lb", "lb", options.Value.LoadBalancerHealthUrl, timeout, ct),
            Http("web", "web", options.Value.WebHealthUrl, timeout, ct),
            ApiAsync(Environment.MachineName, timeout, ct), SharedNode(), EmbeddingsAsync(timeout, ct), Task.FromResult(ChatProvider()),
        };
        var plugins = DeclaredNodes(installed.Installed()).Where(n => !CoreNodeIds.Contains(NodeId(n.Manifest, n.Node)))
            .Select(n => PluginAsync(n.Manifest, n.Node, bearerToken, timeout, ct));
        var nodes = await Task.WhenAll(core.Concat(plugins));
        ct.ThrowIfCancellationRequested();
        return new TopologyReport(time.GetUtcNow(), options.Value.CacheSeconds, nodes.Any(n => n.Instances.Any(i => System.Net.IPAddress.TryParse(i.Address, out _))), (await shared).Instance, nodes);
    }

    private async Task<(TopologyNode Node, string Instance)> SharedAsync(TimeSpan timeout, CancellationToken ct)
    {
        var facts = new Dictionary<string, string> { ["role"] = "shared state" };
        try
        {
            using var budget = Linked(timeout, ct);
            var checks = await health.CheckHealthAsync(c => c.Tags.Contains("shared-state"), budget.Token);
            var entry = checks.Entries.Values.FirstOrDefault();
            var instance = entry.Data?.GetValueOrDefault("instance")?.ToString() ?? Environment.MachineName;
            return (new TopologyNode("redis", "redis", checks.Status == HealthStatus.Healthy ? NodeHealth.Healthy : NodeHealth.Unreachable,
                [], facts, checks.Status == HealthStatus.Healthy ? null : entry.Description), instance);
        }
        catch (Exception ex) { ct.ThrowIfCancellationRequested(); return (new TopologyNode("redis", "redis", NodeHealth.Unreachable,
            [], facts, Describe(ex, timeout)), Environment.MachineName); }
    }

    private async Task<TopologyNode> ApiAsync(string instance, TimeSpan timeout, CancellationToken ct)
    {
        using var budget = Linked(timeout, ct);
        try
        {
            var addresses = await resolver.ResolveAsync(options.Value.ApiService, budget.Token);
            if (addresses.Count > 0) return await ReplicasAsync("api", "api", addresses, "/health", timeout, budget.Token);
            return new TopologyNode("api", "api", NodeHealth.Healthy, [new TopologyInstance(instance, null, NodeHealth.Healthy)],
                new Dictionary<string, string> { ["replicas"] = "1 (discovery unavailable)" }, "replicas could not be discovered; reporting this instance only");
        }
        catch (Exception ex) { ct.ThrowIfCancellationRequested(); return Failed("api", "api", ex, timeout); }
    }

    private async Task<TopologyNode> PluginAsync(PluginManifest manifest, TopologyTable declaration, string bearerToken, TimeSpan timeout, CancellationToken ct)
    {
        var id = NodeId(manifest, declaration);
        var name = declaration.Label ?? id;
        if (declaration.Service == "qdrant") return await QdrantAsync(id, name, timeout, ct);
        if (declaration.Service == "neo4j") return await GraphAsync(id, name, timeout, ct);
        using var budget = Linked(timeout, ct);
        try
        {
            var service = declaration.Service;
            var addresses = string.IsNullOrWhiteSpace(service) ? [] : await resolver.ResolveAsync(service, budget.Token);
            var healthUrl = !string.IsNullOrWhiteSpace(declaration.Url) ? declaration.Url
                : $"http://{service}:{options.Value.ServicePort}{declaration.Health ?? "/health"}";
            var replicaTask = addresses.Count > 0 ? ReplicasAsync(id, name, addresses, declaration.Health ?? new Uri(healthUrl).PathAndQuery, timeout, budget.Token, new Uri(healthUrl).Port)
                : Http(id, name, healthUrl, timeout, budget.Token);
            var endpoint = manifest.Domain is null ? null : installed.McpEndpoint(manifest.Name);
            var toolTask = manifest.Domain is null ? Task.FromResult<ToolFacts?>(null) : ToolsAsync(manifest.Name, endpoint, bearerToken, timeout, budget.Token);
            var cardTask = string.IsNullOrWhiteSpace(declaration.Card) ? Task.FromResult<CardFacts?>(null)
                : CardAsync(healthUrl, declaration.Card, timeout, budget.Token);
            await Task.WhenAll(replicaTask, toolTask, cardTask);
            ct.ThrowIfCancellationRequested();
            var node = await replicaTask;
            var facts = new Dictionary<string, string>(node.Facts);
            var tools = await toolTask;
            var card = await cardTask;
            var errors = new List<string>();
            if (tools is not null)
            {
                facts["endpoint"] = endpoint ?? "not configured";
                facts["domain"] = manifest.Domain!.Id;
                facts["tools"] = tools.Names.Count > 0 ? $"{tools.Names.Count}: {string.Join(", ", tools.Names)}" : "unknown";
                if (tools.Error is not null) errors.Add(tools.Error);
            }
            if (card is not null)
            {
                facts["baseUrl"] = new Uri(new Uri(healthUrl), ".").AbsoluteUri.TrimEnd('/');
                if (card.Name is not null) facts["agent"] = card.Name;
                if (card.Skills is not null) facts["skills"] = card.Skills;
                if (card.Error is not null) errors.Add(card.Error);
            }
            return node with { Facts = facts, Health = errors.Count == 0 ? node.Health : node.Health == NodeHealth.Healthy ? NodeHealth.Degraded : node.Health,
                Reason = errors.Count == 0 ? node.Reason : string.Join("; ", errors) };
        }
        catch (Exception ex) { ct.ThrowIfCancellationRequested(); return Failed(id, name, ex, timeout); }
    }

    private sealed record ToolFacts(IReadOnlyList<string> Names, string? Error);
    private async Task<ToolFacts?> ToolsAsync(string plugin, string? endpoint, string token, TimeSpan timeout, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) return new([], "no MCP endpoint is configured");
        try
        {
            var principal = PluginAccessContext.Principal ?? throw new UnauthorizedAccessException("No validated caller scope.");
            if (tokens is null) throw new UnauthorizedAccessException("No plugin token provider.");
            var exchanged = await tokens.ForAsync(principal, plugin, token, ct);
            await using var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(endpoint), TransportMode = HttpTransportMode.StreamableHttp,
                AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {exchanged}" },
            }, Client(), loggers, ownsHttpClient: true);
            await using var client = await McpClient.CreateAsync(transport, loggerFactory: loggers, cancellationToken: ct);
            var tools = await client.ListToolsAsync(cancellationToken: ct);
            return new([.. tools.Select(t => t.Name).Order()], null);
        }
        catch (Exception ex) { return new([], $"tools/list failed: {Describe(ex, timeout)}"); }
    }

    private sealed record CardFacts(string? Name, string? Skills, string? Error);
    private async Task<CardFacts?> CardAsync(string baseUrl, string path, TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            var card = await Client().GetFromJsonAsync<JsonElement>(new Uri(new Uri(baseUrl), path), ct);
            return new(card.TryGetProperty("name", out var name) ? name.GetString() : null,
                card.TryGetProperty("skills", out var skills) ? string.Join(", ", skills.EnumerateArray().Select(s => s.GetProperty("id").GetString())) : null, null);
        }
        catch (Exception ex) { return new(null, null, Describe(ex, timeout)); }
    }

    private async Task<TopologyNode> ReplicasAsync(string id, string name, IReadOnlyList<string> addresses, string path, TimeSpan timeout, CancellationToken ct, int? port = null) =>
        Aggregate(id, name, await Task.WhenAll(addresses.Select(a => HealthOfAsync(a, path, timeout, ct, port))));

    private async Task<TopologyNode> QdrantAsync(string id, string name, TimeSpan timeout, CancellationToken ct)
    {
        var facts = new Dictionary<string, string> { ["collection"] = qdrant.Value.Collection, ["host"] = $"{qdrant.Value.Host}:{qdrant.Value.GrpcPort}" };
        try
        {
            using var budget = Linked(timeout, ct);
            var description = await vectors.DescribeChunkCollectionAsync(budget.Token);
            facts["chunks"] = description?.Points.ToString() ?? "0";
            if (description is null) return new(id, name, NodeHealth.Degraded, [], facts, $"collection {qdrant.Value.Collection} does not exist; index the corpus");
            facts["status"] = description.Status;
            return new(id, name, NodeHealth.Healthy, [], facts);
        }
        catch (Exception ex) { ct.ThrowIfCancellationRequested(); return new(id, name, NodeHealth.Unreachable, [], facts, Describe(ex, timeout)); }
    }

    private async Task<TopologyNode> GraphAsync(string id, string name, TimeSpan timeout, CancellationToken ct)
    {
        var o = graphOptions.Value;
        var facts = new Dictionary<string, string> { ["role"] = "graph store", ["host"] = o.Authority, ["database"] = o.Database };
        try
        {
            using var budget = Linked(timeout, ct);
            await graph.VerifyConnectivityAsync(budget.Token);
            return new(id, name, NodeHealth.Healthy, [], facts);
        }
        catch (Exception ex) { ct.ThrowIfCancellationRequested(); return new(id, name, NodeHealth.Unreachable, [], facts,
            ex.GetType().Name == "AuthenticationException" ? "credentials refused" : ex.GetType().Name); }
    }

    private static TopologyNode Failed(string id, string name, Exception ex, TimeSpan timeout) =>
        new(id, name, NodeHealth.Unreachable, [], new Dictionary<string, string>(), Describe(ex, timeout));

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
    private async Task<TopologyInstance> HealthOfAsync(string address, string path, TimeSpan timeout, CancellationToken ct, int? port = null)
    {
        var host = address.Contains(':') ? $"[{address}]" : address;
        try
        {
            using var cts = Linked(timeout, ct);
            using var response = await Client().GetAsync($"http://{host}:{port ?? options.Value.ServicePort}{path}", cts.Token);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync(cts.Token);
            string? name = null;
            try
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.TryGetProperty("instance", out var instance)) name = instance.GetString();
            }
            catch (JsonException) when (path != "/health") { /* An HTTP health URL may return text or metrics. */ }
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
