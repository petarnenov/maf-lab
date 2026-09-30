using Maf.Lab.Indexing.Pipeline;
using Maf.Lab.Retrieval;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Maf.Lab.Indexing;

public static class IndexingServiceCollectionExtensions
{
    public const int DefaultEmbeddingTimeoutSeconds = 900;

    public static IServiceCollection AddMafIndexing(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMafRetrievalCore(configuration);
        services.Configure<IndexingOptions>(configuration.GetSection(IndexingOptions.Section));
        // A batch of long chunks on CPU Ollama can outlast HttpClient's 100 s (19 code chunks took 1m40s), and the
        // timeout cancels the run. Configuration still wins when it sets a value.
        services.PostConfigure<Retrieval.Configuration.ModelOptions>(m => m.EmbeddingTimeoutSeconds ??= DefaultEmbeddingTimeoutSeconds);
        services.TryAddSingleton<ContextualEnricherFactory>();
        services.TryAddSingleton<IndexingPipeline>();
        services.TryAddSingleton<DriftService>();
        services.TryAddSingleton<MigrationService>();
        return services;
    }
}
