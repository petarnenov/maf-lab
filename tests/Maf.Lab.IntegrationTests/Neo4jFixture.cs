using Maf.Lab.Indexing;
using Maf.Lab.Retrieval.Graph;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Neo4j.Driver;
using Testcontainers.Neo4j;

namespace Maf.Lab.IntegrationTests;

/// <summary>
/// One Neo4j container (same image as compose) for the graph tests. Community has a single database, so the tests that
/// use it share one collection and run one at a time; each starts from an empty graph.
/// </summary>
public sealed class Neo4jFixture : IAsyncLifetime
{
    public const string Image = "neo4j:2026.09.0-community";
    public const string Password = "maf-lab-test-graph";

    private readonly Neo4jContainer _container = new Neo4jBuilder(Image)
        .WithEnvironment("NEO4J_AUTH", $"neo4j/{Password}")
        .Build();

    public string BoltUri => $"bolt://{_container.Hostname}:{_container.GetMappedPublicPort(7687)}";

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    public Dictionary<string, string?> Config() => new()
    {
        ["Neo4j:Uri"] = BoltUri,
        ["Neo4j:User"] = "neo4j",
        ["Neo4j:Password"] = Password,
    };

    /// <summary>The graph store and the graph build, wired to the container.</summary>
    public ServiceProvider Services(Action<Dictionary<string, string?>>? configure = null)
    {
        var values = Config();
        configure?.Invoke(values);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.Configure<IndexingOptions>(configuration.GetSection(IndexingOptions.Section));
        services.AddGraphStore(configuration);
        services.AddSingleton<Indexing.Graph.GraphBuildService>();
        return services.BuildServiceProvider();
    }

    /// <summary>Empties the graph; each test builds what it needs.</summary>
    public async Task ResetAsync(IServiceProvider services)
    {
        var driver = services.GetRequiredService<IDriver>();
        await driver.ExecutableQuery("MATCH (n) DETACH DELETE n").ExecuteAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class GraphCollection : ICollectionFixture<Neo4jFixture>
{
    public const string Name = "graph";
}
