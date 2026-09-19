using System.Net;
using System.Text.Json;
using Maf.Lab.Api.Topology;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Domain.Topology;
using Maf.Lab.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Maf.Lab.Tests;

/// <summary>
/// The topology report: every node present, health measured rather than assumed, no secrets, and the drawn
/// diagram kept in step with it.
/// </summary>
public class TopologyTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static ApiFactory Api(StubHandler handler, IReadOnlyDictionary<string, string[]>? dns = null)
    {
        var api = new ApiFactory(ApiFactory.ProceduralModel());
        api.ConfigureTestServices = s =>
        {
            s.RemoveAll<IServiceResolver>();
            s.AddSingleton<IServiceResolver>(new StubResolver(dns ?? new Dictionary<string, string[]>()));
            s.AddHttpClient("topology").ConfigurePrimaryHttpMessageHandler(() => handler);
        };
        return api;
    }

    private static async Task<TopologyReport> GetAsync(ApiFactory api, Role role = Role.ADVISOR)
    {
        var response = await api.ClientFor("adam", "firm-a", role).GetAsync("/api/topology", Ct);
        response.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<TopologyReport>(await response.Content.ReadAsStringAsync(Ct), Json)!;
    }

    [Fact]
    public async Task Unauthenticated_requests_are_rejected()
    {
        using var api = Api(StubHandler.AllHealthy());
        var response = await api.CreateClient().GetAsync("/api/topology", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Every_service_is_reported_with_its_edges()
    {
        using var api = Api(StubHandler.AllHealthy());

        var report = await GetAsync(api);

        Assert.Equal(TopologyProbe.NodeIds, [.. report.Nodes.Select(n => n.Id)]);
        Assert.NotEmpty(report.Edges);
        Assert.Contains(report.Edges, e => e is { From: "api", To: "mcp" });
        Assert.Contains(report.Edges, e => e is { From: "mcp", To: "qdrant" });
        Assert.Equal(Environment.MachineName, report.ReportedBy);
        Assert.True(report.GeneratedAt > DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task Replicas_are_discovered_and_named()
    {
        var handler = StubHandler.AllHealthy();
        handler.Health["10.0.0.1"] = ("api-replica-1", true);
        handler.Health["10.0.0.2"] = ("api-replica-2", true);
        handler.Health["10.0.1.1"] = ("mcp-replica-1", true);
        handler.Health["10.0.1.2"] = ("mcp-replica-2", true);
        using var api = Api(handler, new Dictionary<string, string[]>
        {
            ["api"] = ["10.0.0.1", "10.0.0.2"],
            ["mcp-retrieval"] = ["10.0.1.1", "10.0.1.2"],
        });

        var report = await GetAsync(api);

        Assert.True(report.DiscoveryAvailable);
        var apiNode = report.Nodes.Single(n => n.Id == "api");
        Assert.Equal(NodeHealth.Healthy, apiNode.Health);
        Assert.Equal(["api-replica-1", "api-replica-2"], apiNode.Instances.Select(i => i.Name));
        Assert.Equal("2/2 healthy", apiNode.Facts["replicas"]);
        Assert.Equal(["mcp-replica-1", "mcp-replica-2"], report.Nodes.Single(n => n.Id == "mcp").Instances.Select(i => i.Name));
    }

    [Fact]
    public async Task One_silent_replica_makes_the_service_degraded()
    {
        var handler = StubHandler.AllHealthy();
        handler.Health["10.0.0.1"] = ("api-replica-1", true);
        handler.Health["10.0.0.2"] = ("api-replica-2", false);
        using var api = Api(handler, new Dictionary<string, string[]> { ["api"] = ["10.0.0.1", "10.0.0.2"] });

        var node = (await GetAsync(api)).Nodes.Single(n => n.Id == "api");

        Assert.Equal(NodeHealth.Degraded, node.Health);
        Assert.Equal(NodeHealth.Healthy, node.Instances.Single(i => i.Name == "api-replica-1").Health);
        Assert.Equal(NodeHealth.Unreachable, node.Instances.Single(i => i.Address == "10.0.0.2").Health);
        Assert.Contains("10.0.0.2", node.Reason);
    }

    [Fact]
    public async Task Without_discovery_the_report_speaks_for_this_instance_only()
    {
        using var api = Api(StubHandler.AllHealthy());

        var report = await GetAsync(api);
        var node = report.Nodes.Single(n => n.Id == "api");

        Assert.False(report.DiscoveryAvailable);
        Assert.Equal(Environment.MachineName, Assert.Single(node.Instances).Name);
        Assert.Contains("could not be discovered", node.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_unreachable_service_does_not_take_the_report_down_with_it()
    {
        var handler = StubHandler.AllHealthy();
        handler.Fail.Add("lb-health");
        using var api = Api(handler);

        var report = await GetAsync(api);

        var lb = report.Nodes.Single(n => n.Id == "lb");
        Assert.Equal(NodeHealth.Unreachable, lb.Health);
        Assert.NotNull(lb.Reason);
        Assert.Equal(NodeHealth.Healthy, report.Nodes.Single(n => n.Id == "web").Health);
    }

    [Fact]
    public async Task A_service_that_never_answers_is_bounded_by_the_probe_timeout()
    {
        var handler = StubHandler.AllHealthy();
        handler.Hang.Add("healthz");
        using var api = Api(handler);

        var started = DateTimeOffset.UtcNow;
        var report = await GetAsync(api);

        Assert.Equal(NodeHealth.Unreachable, report.Nodes.Single(n => n.Id == "web").Health);
        // The default probe budget is 2 s; the report must not wait for the hanging service beyond it.
        Assert.True(DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task The_embeddings_node_is_degraded_when_a_configured_model_is_not_pulled()
    {
        var handler = StubHandler.AllHealthy();
        handler.PulledModels = ["nomic-embed-text:latest"];
        using var api = Api(handler);

        var node = (await GetAsync(api)).Nodes.Single(n => n.Id == "ollama-embeddings");

        Assert.Equal(NodeHealth.Degraded, node.Health);
        Assert.Contains("all-minilm", node.Reason);
    }

    [Fact]
    public async Task The_chat_provider_is_reported_from_configuration_and_never_carries_the_key()
    {
        using var api = Api(StubHandler.AllHealthy());

        var response = await api.ClientFor("adam", "firm-a", Role.ADVISOR).GetAsync("/api/topology", Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);
        var node = JsonSerializer.Deserialize<TopologyReport>(body, Json)!.Nodes.Single(n => n.Id == "chat-provider");

        Assert.Equal("gpt-oss:120b", node.Facts["chatModel"]);
        Assert.Contains(node.Facts["apiKey"], new[] { "set (OLLAMA_API_KEY)", "not set" });
        var key = Environment.GetEnvironmentVariable("OLLAMA_API_KEY");
        if (!string.IsNullOrWhiteSpace(key))
        {
            Assert.DoesNotContain(key, body);
        }
    }

    [Fact]
    public async Task Health_is_sent_by_name_which_is_what_the_web_app_reads()
    {
        using var api = Api(StubHandler.AllHealthy());

        var body = await api.ClientFor("adam", "firm-a", Role.ADVISOR).GetStringAsync("/api/topology", Ct);

        Assert.Contains("\"health\":\"Healthy\"", body);
        Assert.DoesNotContain("\"health\":0", body);
    }

    [Fact]
    public async Task The_mcp_node_reports_the_tools_it_offers()
    {
        using var api = Api(StubHandler.AllHealthy());

        var node = (await GetAsync(api)).Nodes.Single(n => n.Id == "mcp");

        Assert.StartsWith("3: ", node.Facts["tools"]);
        Assert.Contains("search_documents", node.Facts["tools"]);
        Assert.Equal(NodeHealth.Healthy, node.Health);
    }

    [Fact]
    public async Task A_report_is_reused_within_its_cache_window()
    {
        var handler = StubHandler.AllHealthy();
        using var api = Api(handler);

        var first = await GetAsync(api);
        var probes = handler.Requests;
        var second = await GetAsync(api);

        Assert.Equal(first.GeneratedAt, second.GeneratedAt);
        Assert.Equal(probes, handler.Requests);
        Assert.True(second.CacheSeconds > 0);
    }

    [Fact]
    public async Task The_diagram_is_served_and_holds_exactly_the_reported_nodes()
    {
        using var api = Api(StubHandler.AllHealthy());
        var client = api.ClientFor("adam", "firm-a", Role.ADVISOR);

        var xml = await client.GetStringAsync("/api/topology/diagram", Ct);

        Assert.StartsWith("<?xml", xml);
        Assert.DoesNotContain("<diagram>", xml.Replace(" ", "")); // compressed diagrams have a bare <diagram> holding base64
        var drawn = DiagramIds(xml);
        var reported = TopologyProbe.NodeIds.ToHashSet();
        Assert.True(drawn.SetEquals(reported),
            $"drawn but not reported: [{string.Join(", ", drawn.Except(reported))}]; reported but not drawn: [{string.Join(", ", reported.Except(drawn))}]");
    }

    /// <summary>Vertex ids of the committed diagram (edges and the two mxGraph roots are not nodes).</summary>
    private static HashSet<string> DiagramIds(string xml) =>
    [
        .. System.Text.RegularExpressions.Regex.Matches(xml, """<mxCell id="([^"]+)"[^>]*vertex="1""")
            .Select(m => m.Groups[1].Value),
    ];
}

/// <summary>Resolves the compose service names a test pretends to have.</summary>
internal sealed class StubResolver(IReadOnlyDictionary<string, string[]> dns) : IServiceResolver
{
    public Task<IReadOnlyList<string>> ResolveAsync(string service, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<string>>(dns.TryGetValue(service, out var addresses) ? addresses : []);
}

/// <summary>Answers the probe's HTTP calls: /health per replica, /lb-health, /healthz and Ollama's /api/tags.</summary>
internal sealed class StubHandler : HttpMessageHandler
{
    public Dictionary<string, (string Instance, bool Healthy)> Health { get; } = [];
    public HashSet<string> Fail { get; } = [];
    public HashSet<string> Hang { get; } = [];
    public string[] PulledModels { get; set; } = ["nomic-embed-text:latest", "all-minilm:latest"];
    public int Requests;

    public static StubHandler AllHealthy() => new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Interlocked.Increment(ref Requests);
        var url = request.RequestUri!.ToString();
        if (Hang.Any(url.Contains))
        {
            await Task.Delay(TimeSpan.FromMinutes(1), ct);
        }
        if (Fail.Any(url.Contains))
        {
            return new HttpResponseMessage(HttpStatusCode.BadGateway);
        }
        if (url.Contains("/api/tags"))
        {
            return Json($$"""{"models":[{{string.Join(",", PulledModels.Select(m => $$"""{"model":"{{m}}"}"""))}}]}""");
        }
        if (url.Contains("/health") && !url.Contains("lb-health"))
        {
            var host = request.RequestUri.Host;
            if (Health.TryGetValue(host, out var known))
            {
                return known.Healthy ? Json($$"""{"status":"ok","instance":"{{known.Instance}}"}""") : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }
            return Json($$"""{"status":"ok","instance":"{{host}}"}""");
        }
        return Json("""{"status":"ok"}""");
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
}
