using Maf.Lab.Indexing;
using Maf.Lab.Retrieval.Models;
using Maf.Lab.TestSupport;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Qdrant.Client;
using Testcontainers.Qdrant;

[assembly: AssemblyFixture(typeof(Maf.Lab.IntegrationTests.QdrantFixture))]

namespace Maf.Lab.IntegrationTests;

/// <summary>One Qdrant container (same version as compose) for the whole test assembly; tests isolate by collection name.</summary>
public sealed class QdrantFixture : IAsyncLifetime
{
    public const string Image = "qdrant/qdrant:v1.19.1";
    private readonly QdrantContainer _container = new QdrantBuilder(Image).Build();

    public string Host => _container.Hostname;
    public int GrpcPort => _container.GetMappedPublicPort(6334);

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    public QdrantClient RawClient() => new(Host, GrpcPort);

    public Dictionary<string, string?> Config(string collection, string? corpusRoot = null) => new()
    {
        ["Qdrant:Host"] = Host,
        ["Qdrant:GrpcPort"] = GrpcPort.ToString(),
        ["Qdrant:Collection"] = collection,
        ["Qdrant:MetaCollection"] = collection + "_meta",
        ["Indexing:CorpusRoot"] = corpusRoot,
    };

    /// <summary>Indexing + retrieval services wired to the container with the deterministic fake embedder.</summary>
    public ServiceProvider Services(string collection, string? corpusRoot = null, Action<Dictionary<string, string?>>? configure = null,
        IChatClient? chat = null, Action<IServiceCollection>? services = null, ILoggerProvider? logs = null)
    {
        var values = Config(collection, corpusRoot);
        configure?.Invoke(values);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var collectionServices = new ServiceCollection();
        collectionServices.AddLogging(b =>
        {
            b.SetMinimumLevel(LogLevel.Debug);
            if (logs is not null)
            {
                b.AddProvider(logs);
            }
        });
        collectionServices.AddSingleton<IDenseEncoder>(FakeDenseEncoder.Default());
        if (chat is not null)
        {
            collectionServices.AddSingleton<IChatClientFactory>(new FixedChatClientFactory(chat));
        }
        services?.Invoke(collectionServices);
        collectionServices.AddMafIndexing(configuration);
        return collectionServices.BuildServiceProvider();
    }
}
