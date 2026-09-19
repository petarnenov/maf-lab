using Maf.Lab.Api.Agent;
using Maf.Lab.Api.Topology;
using Maf.Lab.Domain.Topology;
using Maf.Lab.Retrieval.Configuration;
using Maf.Lab.Retrieval.Store;
using Maf.Lab.TestSupport;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Qdrant.Client;

namespace Maf.Lab.IntegrationTests;

/// <summary>The store probe against a real Qdrant: what it reports for a healthy, an empty and a missing collection.</summary>
public class TopologyProbeTests(QdrantFixture qdrant)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static TopologyProbe Probe(QdrantClient client, string collection) =>
        new(Options.Create(new TopologyOptions { ApiService = "", McpService = "", ProbeTimeoutSeconds = 2, CacheSeconds = 0 }),
            Options.Create(new QdrantOptions { Collection = collection, Host = "test", GrpcPort = 6334 }),
            Options.Create(new ModelOptions()),
            Options.Create(new AgentOptions()),
            new FakeToolSource(),
            client,
            new StubHttpClientFactory(),
            new MemoryCache(new MemoryCacheOptions()),
            TimeProvider.System,
            new NoServiceResolver(),
            NullLoggerFactory.Instance);

    private static TopologyNode Store(TopologyReport report) => report.Nodes.Single(n => n.Id == "qdrant");

    [Fact]
    public async Task An_indexed_collection_is_healthy_and_reports_its_size()
    {
        var collection = $"topo_{Guid.NewGuid():N}";
        await using var services = qdrant.Services(collection);
        await services.GetRequiredService<CollectionBootstrapper>().EnsureAsync(Ct);

        var node = Store(await Probe(qdrant.RawClient(), collection).GetAsync("token", Ct));

        Assert.Equal(NodeHealth.Healthy, node.Health);
        Assert.Equal(collection, node.Facts["collection"]);
        Assert.Equal("0", node.Facts["chunks"]);
        Assert.Null(node.Reason);
        Assert.NotNull(node.LatencyMs);
    }

    [Fact]
    public async Task A_missing_collection_is_degraded_not_unreachable()
    {
        var node = Store(await Probe(qdrant.RawClient(), $"never_indexed_{Guid.NewGuid():N}").GetAsync("token", Ct));

        Assert.Equal(NodeHealth.Degraded, node.Health);
        Assert.Contains("does not exist", node.Reason);
        Assert.Equal("0", node.Facts["chunks"]);
    }

    [Fact]
    public async Task A_stopped_store_is_unreachable_and_the_rest_of_the_report_survives()
    {
        // Nothing listens on this port; the probe must give up within its budget and still return a full report.
        var report = await Probe(new QdrantClient("127.0.0.1", 1), "maf_chunks").GetAsync("token", Ct);

        var node = Store(report);
        Assert.Equal(NodeHealth.Unreachable, node.Health);
        Assert.NotNull(node.Reason);
        Assert.Equal(TopologyProbe.NodeIds, [.. report.Nodes.Select(n => n.Id)]);
    }
}

/// <summary>No DNS: the probe reports this instance only, which is what a single process is.</summary>
internal sealed class NoServiceResolver : IServiceResolver
{
    public Task<IReadOnlyList<string>> ResolveAsync(string service, CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>([]);
}

/// <summary>HTTP probes have nothing to talk to here; they fail fast and are covered by the unit tests.</summary>
internal sealed class StubHttpClientFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(new SocketsHttpHandler()) { Timeout = TimeSpan.FromSeconds(2) };
}
