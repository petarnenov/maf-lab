using Maf.Lab.Domain.Services;
using Maf.Lab.Hosting;
using Maf.Lab.Api.Agent;
using Maf.Lab.Plugins.Topology;
using Maf.Lab.Plugins.Abstractions;
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

    private async Task<TopologyReport> ReportAsync(QdrantClient client, string collection)
    {
        await using var services = qdrant.Services(collection);
        var vectors = ActivatorUtilities.CreateInstance<CollectionBootstrapper>(services, client,
            Options.Create(new QdrantOptions { Collection = collection, Host = "test", GrpcPort = 6334 }));
        var probe = new TopologyProbe(Options.Create(new TopologyOptions { ApiService = "", ProbeTimeoutSeconds = 2, CacheSeconds = 0 }),
            Options.Create(new QdrantOptions { Collection = collection, Host = "test", GrpcPort = 6334 }), Options.Create(new ModelOptions()), vectors,
            new Maf.Lab.Retrieval.Graph.TenantScopedGraphMaintenance(Neo4j.Driver.GraphDatabase.Driver("bolt://127.0.0.1:1", Neo4j.Driver.AuthTokens.None),
                Options.Create(new Maf.Lab.Retrieval.Graph.GraphOptions { Uri = "bolt://127.0.0.1:1" })),
            Options.Create(new Maf.Lab.Retrieval.Graph.GraphOptions { Uri = "bolt://127.0.0.1:1" }),
            new StubHttpClientFactory(), new MemoryCache(new MemoryCacheOptions()),
            new ServiceCollection().AddLogging().AddInstanceHealth().Services.BuildServiceProvider().GetRequiredService<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckService>(),
            TimeProvider.System, new NoServiceResolver(), NullLoggerFactory.Instance, new InstalledVectorStore());
        return await probe.GetAsync("token", Ct);
    }

    private static TopologyNode Store(TopologyReport report) => report.Nodes.Single(n => n.Id == "qdrant");

    [Fact]
    public async Task An_indexed_collection_is_healthy_and_reports_its_size()
    {
        var collection = $"topo_{Guid.NewGuid():N}";
        await using var services = qdrant.Services(collection);
        await services.GetRequiredService<CollectionBootstrapper>().EnsureAsync(Ct);

        var node = Store(await ReportAsync(qdrant.RawClient(), collection));

        Assert.Equal(NodeHealth.Healthy, node.Health);
        Assert.Equal(collection, node.Facts["collection"]);
        Assert.Equal("0", node.Facts["chunks"]);
        Assert.Null(node.Reason);
    }

    [Fact]
    public async Task A_missing_collection_is_degraded_not_unreachable()
    {
        var node = Store(await ReportAsync(qdrant.RawClient(), $"never_indexed_{Guid.NewGuid():N}"));

        Assert.Equal(NodeHealth.Degraded, node.Health);
        Assert.Contains("does not exist", node.Reason);
        Assert.Equal("0", node.Facts["chunks"]);
    }

    [Fact]
    public async Task A_stopped_store_is_unreachable_and_the_rest_of_the_report_survives()
    {
        // Nothing listens on this port; the probe must give up within its budget and still return a full report.
        var report = await ReportAsync(new QdrantClient("127.0.0.1", 1), "maf_chunks");

        var node = Store(report);
        Assert.Equal(NodeHealth.Unreachable, node.Health);
        Assert.NotNull(node.Reason);
        Assert.Equal(TopologyProbe.CoreNodeIds.Append("qdrant"), report.Nodes.Select(n => n.Id));
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

internal sealed class InstalledVectorStore : IInstalledPlugins
{
    public bool IsInstalled(string plugin) => plugin == "qdrant";
    public string? McpEndpoint(string plugin) => null;
    public IReadOnlyList<PluginManifest> Installed() => [new() { Name = "qdrant", Topology = new() { Service = "qdrant" } }];
}
