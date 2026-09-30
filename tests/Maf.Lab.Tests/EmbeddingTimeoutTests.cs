using Maf.Lab.Indexing;
using Maf.Lab.Retrieval.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Tests;

public class EmbeddingTimeoutTests
{
    [Fact]
    public void Indexer_gives_embedding_requests_a_longer_timeout_than_httpclients_default()
    {
        Assert.Equal(IndexingServiceCollectionExtensions.DefaultEmbeddingTimeoutSeconds, TimeoutFor(new Dictionary<string, string?>()));
        Assert.True(IndexingServiceCollectionExtensions.DefaultEmbeddingTimeoutSeconds > 100);
    }

    [Fact]
    public void Configured_embedding_timeout_wins_over_the_indexer_default()
    {
        Assert.Equal(42, TimeoutFor(new Dictionary<string, string?> { ["Models:EmbeddingTimeoutSeconds"] = "42" }));
    }

    private static int? TimeoutFor(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        using var provider = new ServiceCollection().AddLogging().AddMafIndexing(configuration).BuildServiceProvider();
        return provider.GetRequiredService<IOptions<ModelOptions>>().Value.EmbeddingTimeoutSeconds;
    }
}
