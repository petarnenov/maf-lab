using Maf.Lab.Domain.Services;
using System.Net;
using System.Collections.Concurrent;
using System.Text.Json;
using Maf.Lab.Plugins.Topology;
using Maf.Lab.Plugins.Abstractions;
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

    /// <summary>The graph store's infra plugin, as an installed set carries it.</summary>
    private static readonly Maf.Lab.Plugins.Abstractions.PluginManifest GraphStore = new()
    {
        Topology = new TopologyTable { Service = "neo4j" }, Name = "neo4j", Kind = "infra", Scope = "installation", Environments = ["dev"], Description = "the graph store",
        Progress = "None", Stopping = "None",
    };

    /// <summary>The vector store's infra plugin, installed by default here: most of these tests probe it.</summary>
    private static readonly Maf.Lab.Plugins.Abstractions.PluginManifest VectorStore = GraphStore with { Topology = new TopologyTable { Service = "qdrant" }, Name = "qdrant", Description = "the vector store" };

    /// <summary>The telemetry stack's plugin, as an installed set carries it.</summary>
    private static readonly Maf.Lab.Plugins.Abstractions.PluginManifest TelemetryStack = GraphStore with { Topology = new TopologyTable { Service = "otel-collector", Url = "http://otel-collector:8889/metrics", Nodes = [new() { Service = "prometheus", Url = "http://prometheus:9090/-/healthy" }, new() { Service = "jaeger", Url = "http://jaeger:16686/jaeger/" }] }, Name = "observability", Kind = "app", Description = "the telemetry stack" };

    private static ApiFactory Api(StubHandler handler, IReadOnlyDictionary<string, string[]>? dns = null,
        string complianceUrl = "http://compliance", FakeToolSource? tools = null, string testAgentUrl = "",
        IReadOnlyDictionary<string, string?>? settings = null, bool withCodeDomain = false, bool withGraphStore = false, bool withVectorStore = true,
        bool withTelemetryStack = false)
    {
        var extra = new Dictionary<string, string?>
        {
            ["A2A:Clients:compliance:BaseUrl"] = complianceUrl,
            ["TestAgent:BaseUrl"] = testAgentUrl,
            // The stand-in billing domain's server, as the billing plugin's server.json would name it.
            ["Agent:Servers:billing:Domain"] = "billing",
            ["Agent:Servers:billing:Endpoint"] = "http://localhost:5090/mcp",
        };
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            extra[key] = value;
        }
        extra["Agent:Servers:fixture-billing:Domain"] = "billing";
        extra["Agent:Servers:fixture-billing:Endpoint"] = "http://localhost:5090/mcp";
        extra["Agent:Servers:fixture-portfolio:Domain"] = "portfolio";
        extra["Agent:Servers:fixture-portfolio:Endpoint"] = "http://localhost:5091/mcp";
        extra["Agent:Servers:fixture-code:Domain"] = "codebase";
        extra["Agent:Servers:fixture-code:Endpoint"] = "http://localhost:5092/mcp";
        var helper = GraphStore with { Name = "fixture-services", Topology = new TopologyTable
        {
            Service = "compliance", Url = complianceUrl, Card = "/.well-known/agent-card.json",
            Nodes = [new() { Service = "test-agent", Url = testAgentUrl, Card = "/.well-known/agent-card.json" }, new() { Service = "coverage-runner", Health = "/health" }],
        } };
        var api = new ApiFactory(ApiFactory.ProceduralModel(), tools)
        {
            ExtraSettings = extra,
            InstalledPlugins = [TopologyPluginSupport.Manifest,
                StandInDomains.BillingManifest with { Topology = new TopologyTable { Id = "mcp", Service = "mcp-retrieval", Health = "/health" } },
                StandInDomains.PortfolioManifest with { Topology = new TopologyTable { Service = "mcp-portfolio", Health = "/health" } }, helper,
                .. withVectorStore ? [VectorStore] : Array.Empty<PluginManifest>(),
                .. withGraphStore ? [GraphStore] : Array.Empty<PluginManifest>(),
                .. withTelemetryStack ? [TelemetryStack] : Array.Empty<PluginManifest>(),
                .. withCodeDomain ? [GraphStore with { Name = "fixture-code", Kind = "mcp", Domain = new DomainTable { Id = "codebase", Tools = ["search_codebase", "get_code_symbols", "trace_code_symbol"] }, Topology = new TopologyTable { Service = "mcp-code", Health = "/health" } }] : Array.Empty<PluginManifest>(),
            ],
        };
        api.ConfigureTestServices = s =>
        {
            s.RemoveAll<IServiceResolver>();
            s.AddSingleton<IServiceResolver>(new StubResolver(dns ?? new Dictionary<string, string[]>()));
            s.AddHttpClient("topology").ConfigurePrimaryHttpMessageHandler(() => handler);
            if (withCodeDomain)
            {
                // A code domain in use, as the code plugin would bring one; the core reads its shape, not the plugin.
                s.RemoveAll<Maf.Lab.Api.Agent.DomainCatalogue>();
                s.AddSingleton(Maf.Lab.Api.Agent.DomainCatalogue.Of([.. StandInDomains.WithBilling.All, StandInDomains.CodeDomain],
                    StandInDomains.WithBilling.Behaviours));
            }
        };
        return api;
    }

    private static async Task<TopologyReport> GetAsync(ApiFactory api, Role role = Role.USER)
    {
        var response = await api.ClientFor("adam", "firm-a", role).GetAsync("/api/topology", Ct);
        response.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<TopologyReport>(await response.Content.ReadAsStringAsync(Ct), Json)!;
    }

    [Fact]
    public async Task The_test_generation_services_are_part_of_the_stack()
    {
        using var api = Api(StubHandler.AllHealthy(),
            new Dictionary<string, string[]> { ["test-agent"] = ["10.0.0.21"], ["coverage-runner"] = ["10.0.0.22"] },
            testAgentUrl: "http://test-agent:8080");

        var report = await GetAsync(api);

        var agent = Assert.Single(report.Nodes, n => n.Id == "test-agent");
        var runner = Assert.Single(report.Nodes, n => n.Id == "coverage-runner");
        Assert.Equal((NodeHealth.Healthy, NodeHealth.Healthy), (agent.Health, runner.Health));
        Assert.Single(agent.Instances);
        Assert.Single(runner.Instances);
    }

    [Fact]
    public async Task A_test_agent_uses_its_manifest_without_client_configuration()
    {
        using var api = Api(StubHandler.AllHealthy());

        var node = Assert.Single((await GetAsync(api)).Nodes, n => n.Id == "test-agent");

        Assert.Equal(NodeHealth.Healthy, node.Health);
        Assert.Contains("agent", node.Facts.Keys);
    }

    [Fact]
    public async Task The_second_agent_is_reported_with_what_its_card_says()
    {
        using var api = Api(StubHandler.AllHealthy(),
            new Dictionary<string, string[]> { ["compliance"] = ["10.0.0.7", "10.0.0.8"] });

        var report = await GetAsync(api);

        var node = Assert.Single(report.Nodes, n => n.Id == "compliance");
        Assert.Equal(NodeHealth.Healthy, node.Health);
        Assert.Equal(2, node.Instances.Count);
        Assert.Equal("maf-lab compliance reviewer", node.Facts["agent"]);
        Assert.Equal("review_fee_adjustment", node.Facts["skills"]);
    }

    [Fact]
    public async Task A_compliance_agent_that_does_not_answer_is_reported_as_such()
    {
        var handler = StubHandler.AllHealthy();
        handler.Fail.Add("agent-card.json");
        using var api = Api(handler, new Dictionary<string, string[]> { ["compliance"] = ["10.0.0.7"] });

        var report = await GetAsync(api);

        var node = Assert.Single(report.Nodes, n => n.Id == "compliance");
        Assert.Equal(NodeHealth.Degraded, node.Health);
        Assert.False(string.IsNullOrWhiteSpace(node.Reason));
    }

    [Fact]
    public async Task A_compliance_agent_uses_its_manifest_without_client_configuration()
    {
        using var api = Api(StubHandler.AllHealthy(), complianceUrl: "");

        var report = await GetAsync(api);

        var node = Assert.Single(report.Nodes, n => n.Id == "compliance");
        Assert.Equal(NodeHealth.Healthy, node.Health);
        Assert.Contains("agent", node.Facts.Keys);
    }

    [Fact]
    public async Task Unauthenticated_requests_are_rejected()
    {
        using var api = Api(StubHandler.AllHealthy());
        var response = await api.CreateClient().GetAsync("/api/topology", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_codebase_server_is_reported_with_its_replicas_and_tools()
    {
        using var api = Api(StubHandler.AllHealthy(), new Dictionary<string, string[]> { ["mcp-code"] = ["10.0.0.21", "10.0.0.22"] },
            tools: new FakeToolSource { WithCodebase = true }, withCodeDomain: true, settings: new Dictionary<string, string?>
            {
                // A code domain's server, configured (no plugin manifest here to take the domain from).
                ["Agent:Servers:codebase:Domain"] = "codebase",
                ["Agent:Servers:codebase:Endpoint"] = "http://localhost:5092/mcp",
            });

        var report = await GetAsync(api);

        var node = Assert.Single(report.Nodes, n => n.Id == "mcp-code");
        Assert.Equal(NodeHealth.Healthy, node.Health);
        Assert.Equal(2, node.Instances.Count);
        Assert.Equal("codebase", node.Facts["domain"]);
        Assert.Contains("search_codebase", node.Facts["tools"]);
        Assert.Equal("http://localhost:5092/mcp", node.Facts["endpoint"]);
    }

    [Fact]
    public async Task A_code_domain_that_is_not_installed_is_reported_as_such_not_as_a_fault()
    {
        using var api = Api(StubHandler.AllHealthy());

        Assert.DoesNotContain((await GetAsync(api)).Nodes, node => node.Id == "mcp-code");
    }

    [Fact]
    public async Task The_graph_store_is_reported_without_its_password_or_driver_message()
    {
        using var api = Api(StubHandler.AllHealthy(), withGraphStore: true, settings: new Dictionary<string, string?>
        {
            ["Neo4j:Uri"] = "bolt://127.0.0.1:1",
            ["Neo4j:Password"] = "s3cret-graph",
        });

        var report = await GetAsync(api);

        var node = Assert.Single(report.Nodes, n => n.Id == "neo4j");
        Assert.Equal(NodeHealth.Unreachable, node.Health);
        Assert.Equal(("graph store", "127.0.0.1:1"), (node.Facts["role"], node.Facts["host"]));
        Assert.DoesNotContain("s3cret-graph", JsonSerializer.Serialize(report, Json));
        Assert.Matches("^[A-Za-z0-9]+Exception$", node.Reason!);
    }

    [Fact]
    public async Task A_vector_store_that_is_not_installed_is_reported_as_such_not_as_a_fault()
    {
        using var api = Api(StubHandler.AllHealthy(), withVectorStore: false);

        Assert.DoesNotContain((await GetAsync(api)).Nodes, node => node.Id == "qdrant");
    }

    [Fact]
    public async Task A_graph_store_that_is_not_installed_is_reported_as_such_not_as_a_fault()
    {
        using var api = Api(StubHandler.AllHealthy());

        Assert.DoesNotContain((await GetAsync(api)).Nodes, node => node.Id == "neo4j");
    }

    [Fact]
    public async Task A_telemetry_stack_that_is_not_installed_is_reported_as_such_not_as_a_fault()
    {
        using var api = Api(StubHandler.AllHealthy());

        Assert.DoesNotContain((await GetAsync(api)).Nodes, node => node.Id == "otel-collector");
        Assert.DoesNotContain((await GetAsync(api)).Nodes, node => node.Id == "prometheus");
        Assert.DoesNotContain((await GetAsync(api)).Nodes, node => node.Id == "jaeger");
    }

    [Fact]
    public async Task A_telemetry_stack_that_is_installed_is_probed()
    {
        using var api = Api(StubHandler.AllHealthy(), withTelemetryStack: true);

        var nodes = (await GetAsync(api)).Nodes.Where(n => n.Id is "otel-collector" or "prometheus" or "jaeger").ToList();

        Assert.Equal(3, nodes.Count);
        Assert.All(nodes, node => Assert.Equal(NodeHealth.Healthy, node.Health));
    }

    [Fact]
    public async Task Every_declared_service_is_reported()
    {
        using var api = Api(StubHandler.AllHealthy());

        var report = await GetAsync(api);

        Assert.Equal(ExpectedIds(api).Order(), report.Nodes.Select(n => n.Id).Order());
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
        handler.PulledModels = ["some-other-model:latest"];
        using var api = Api(handler);

        var node = (await GetAsync(api)).Nodes.Single(n => n.Id == "ollama-embeddings");

        Assert.Equal(NodeHealth.Degraded, node.Health);
        Assert.Contains("embeddinggemma", node.Reason);
    }

    [Fact]
    public async Task The_embeddings_node_lists_the_interactive_and_batch_instances()
    {
        using var api = Api(StubHandler.AllHealthy(), settings: BatchEmbeddings);

        var node = (await GetAsync(api)).Nodes.Single(n => n.Id == "ollama-embeddings");

        Assert.Equal(NodeHealth.Healthy, node.Health);
        Assert.Equal(["interactive", "batch"], node.Instances.Select(i => i.Name));
        Assert.All(node.Instances, i => Assert.Equal(NodeHealth.Healthy, i.Health));
        Assert.Equal("http://ollama-batch:11434", node.Facts["batchEndpoint"]);
    }

    [Fact]
    public async Task A_silent_batch_instance_degrades_the_embeddings_node_and_says_which()
    {
        var handler = StubHandler.AllHealthy();
        handler.Fail.Add("ollama-batch");
        using var api = Api(handler, settings: BatchEmbeddings);

        var node = (await GetAsync(api)).Nodes.Single(n => n.Id == "ollama-embeddings");

        Assert.Equal(NodeHealth.Degraded, node.Health);
        Assert.Equal(NodeHealth.Healthy, node.Instances.Single(i => i.Name == "interactive").Health);
        Assert.Equal(NodeHealth.Unreachable, node.Instances.Single(i => i.Name == "batch").Health);
        Assert.StartsWith("batch:", node.Reason);
    }

    [Fact]
    public async Task Without_a_batch_endpoint_the_embeddings_node_has_one_instance_and_is_not_degraded_for_it()
    {
        using var api = Api(StubHandler.AllHealthy());

        var node = (await GetAsync(api)).Nodes.Single(n => n.Id == "ollama-embeddings");

        Assert.Equal(NodeHealth.Healthy, node.Health);
        Assert.Equal("interactive", Assert.Single(node.Instances).Name);
    }

    private static readonly IReadOnlyDictionary<string, string?> BatchEmbeddings =
        new Dictionary<string, string?> { ["Models:BatchOllamaEndpoint"] = "http://ollama-batch:11434" };

    [Fact]
    public async Task The_chat_provider_is_reported_from_configuration_and_never_carries_the_key()
    {
        using var api = Api(StubHandler.AllHealthy());

        var response = await api.ClientFor("adam", "firm-a", Role.USER).GetAsync("/api/topology", Ct);
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

        var body = await api.ClientFor("adam", "firm-a", Role.USER).GetStringAsync("/api/topology", Ct);

        Assert.Contains("\"health\":\"Healthy\"", body);
        Assert.DoesNotContain("\"health\":0", body);
    }

    [Fact]
    public async Task The_mcp_node_reports_the_tools_it_offers()
    {
        using var api = Api(StubHandler.AllHealthy());

        var node = (await GetAsync(api)).Nodes.Single(n => n.Id == "mcp");

        Assert.StartsWith("4: ", node.Facts["tools"]);
        Assert.Contains("search_documents", node.Facts["tools"]);
        Assert.Contains(Maf.Lab.Domain.Billing.FeeAdjustmentTool.Name, node.Facts["tools"]);
        Assert.Equal(NodeHealth.Healthy, node.Health);
    }

    [Fact]
    public async Task A_report_is_reused_within_its_cache_window()
    {
        var handler = StubHandler.AllHealthy();
        using var api = Api(handler);

        var client = api.ClientFor("adam", "firm-a", Role.USER);
        async Task<TopologyReport> Read() => JsonSerializer.Deserialize<TopologyReport>(await client.GetStringAsync("/api/topology", Ct), Json)!;
        var first = await Read();
        var probes = handler.Requests;
        var second = await Read();

        Assert.Equal(first.GeneratedAt, second.GeneratedAt);
        Assert.Equal(probes, handler.Requests);
        Assert.True(second.CacheSeconds > 0);
    }

    [Fact]
    public async Task The_diagram_is_served_and_holds_exactly_the_reported_nodes()
    {
        using var api = Api(StubHandler.AllHealthy(), withCodeDomain: true, withGraphStore: true, withTelemetryStack: true);
        var client = api.ClientFor("adam", "firm-a", Role.USER);

        var response = await client.GetAsync("/api/topology/diagram", Ct);
        var xml = await response.Content.ReadAsStringAsync(Ct);

        Assert.StartsWith("<?xml", xml);
        // A redrawn diagram has to reach the page. Without this a browser decides for itself how long the old
        // one stays fresh, and answers from its own cache without asking.
        Assert.True(response.Headers.CacheControl?.NoCache, $"{response.Headers.CacheControl}");
        Assert.DoesNotContain("<diagram>", xml.Replace(" ", "")); // compressed diagrams have a bare <diagram> holding base64
        var drawn = DiagramIds(xml);
        var reported = (await GetAsync(api)).Nodes.Select(n => n.Id).ToHashSet();
        Assert.True(drawn.SetEquals(reported),
            $"drawn but not reported: [{string.Join(", ", drawn.Except(reported))}]; reported but not drawn: [{string.Join(", ", reported.Except(drawn))}]");
    }

    [Fact]
    public async Task No_two_boxes_in_the_diagram_overlap()
    {
        using var api = Api(StubHandler.AllHealthy());
        var client = api.ClientFor("adam", "firm-a", Role.USER);

        var xml = await client.GetStringAsync("/api/topology/diagram", Ct);
        var boxes = Boxes(xml);

        // A box drawn over another hides whatever is under it — which is how the compliance agent's health line
        // disappeared behind the chat provider.
        foreach (var (first, second) in boxes.SelectMany((a, i) => boxes.Skip(i + 1).Select(b => (a, b))))
        {
            var apart = first.Right <= second.Left || second.Right <= first.Left
                || first.Bottom <= second.Top || second.Bottom <= first.Top;
            Assert.True(apart, $"{first.Id} and {second.Id} overlap in the embedded drawing");
        }
    }

    [Fact]
    public async Task Protocol_probes_use_audience_tokens_and_list_each_servers_tools_once()
    {
        var handler = StubHandler.AllHealthy();
        using var api = Api(handler);
        var client = api.ClientFor("adam", "firm-a", Role.USER);
        var token = client.DefaultRequestHeaders.Authorization!.ToString();
        (await client.GetAsync("/api/topology", Ct)).EnsureSuccessStatusCode();
        Assert.Equal(2, handler.ProtocolRequests.Count(r => r.Method == "tools/list"));
        Assert.All(handler.ProtocolRequests, r =>
        {
            Assert.NotEqual(token, r.Authorization);
            var scoped = new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(r.Authorization!["Bearer ".Length..]);
            Assert.Single(scoped.Audiences);
            Assert.Contains(scoped.Audiences.Single(), new[] { "fixture-billing", "fixture-portfolio" });
        });
        Assert.All(handler.ProtocolRequests, r => Assert.Contains(r.Method, new[] { "server/discover", "tools/list" }));
    }

    [Fact]
    public async Task A_plain_text_http_health_url_can_report_a_healthy_replica()
    {
        var handler = StubHandler.AllHealthy();
        handler.PlainMetrics = true;
        using var api = Api(handler, new Dictionary<string, string[]> { ["otel-collector"] = ["10.0.0.9"] }, withTelemetryStack: true);
        var collector = Assert.Single((await GetAsync(api)).Nodes, n => n.Id == "otel-collector");
        Assert.Equal(NodeHealth.Healthy, collector.Health);
        Assert.Equal("10.0.0.9", Assert.Single(collector.Instances).Name);
    }

    private static IEnumerable<string> ExpectedIds(ApiFactory api) => TopologyProbe.CoreNodeIds.Concat(
        TopologyProbe.DeclaredNodes(api.InstalledPlugins).Select(n => n.Node.Id ?? n.Node.Service ?? n.Manifest.Name));

    private sealed record Box(string Id, double Left, double Top, double Right, double Bottom);

    private static List<Box> Boxes(string xml)
    {
        var document = System.Xml.Linq.XDocument.Parse(xml);
        var boxes = new List<Box>();
        foreach (var cell in document.Descendants("mxCell").Where(c => (string?)c.Attribute("vertex") == "1"))
        {
            var geometry = cell.Element("mxGeometry");
            if (geometry is null)
            {
                continue;
            }
            double Number(string name) =>
                double.TryParse((string?)geometry.Attribute(name), System.Globalization.CultureInfo.InvariantCulture,
                    out var value) ? value : 0;
            var (x, y) = (Number("x"), Number("y"));
            boxes.Add(new Box((string?)cell.Attribute("id") ?? "", x, y, x + Number("width"), y + Number("height")));
        }
        return boxes;
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
    public string[] PulledModels { get; set; } = ["embeddinggemma:latest"];
    public int Requests;
    public bool PlainMetrics { get; set; }
    public ConcurrentBag<(string Method, string? Authorization)> ProtocolRequests { get; } = [];

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
        if (request.RequestUri.AbsolutePath == "/mcp")
        {
            if (request.Method != HttpMethod.Post) return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);
            var message = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement;
            if (!message.TryGetProperty("id", out var id)) return new HttpResponseMessage(HttpStatusCode.Accepted);
            var method = message.GetProperty("method").GetString();
            ProtocolRequests.Add((method!, request.Headers.Authorization?.ToString()));
            var names = request.RequestUri.Port == 5092 ? new[] { "search_codebase", "get_code_symbols", "trace_code_symbol" }
                : request.RequestUri.Port == 5091 ? new[] { "search_portfolio_documents" }
                : new[] { "search_documents", "get_billing_run_status", "search_billing_runs", "propose_fee_adjustment" };
            object result = method == "server/discover" ? new { supportedVersions = new[] { "2026-07-28" }, capabilities = new { tools = new { } }, ttlMs = 0, cacheScope = "private" }
                : method == "initialize" ? new { protocolVersion = "2025-11-25", capabilities = new { tools = new { } }, serverInfo = new { name = "fixture", version = "1.0" } }
                : new { tools = names.Select(name => new { name, inputSchema = new { type = "object", properties = new { } } }), ttlMs = 0, cacheScope = "private" };
            return Json(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result }));
        }
        if (PlainMetrics && request.RequestUri.AbsolutePath == "/metrics") return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("healthy_metric 1") };
        if (url.Contains("/api/tags"))
        {
            return Json($$"""{"models":[{{string.Join(",", PulledModels.Select(m => $$"""{"model":"{{m}}"}"""))}}]}""");
        }
        if (url.Contains("agent-card.json"))
        {
            return Json("""
                {"name":"maf-lab compliance reviewer","skills":[{"id":"review_fee_adjustment"}]}
                """);
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
