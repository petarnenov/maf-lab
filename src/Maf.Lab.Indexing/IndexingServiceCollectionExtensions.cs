using Maf.Lab.Indexing.Pipeline;
using Maf.Lab.Retrieval;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Maf.Lab.Indexing;

public static class IndexingServiceCollectionExtensions
{
    public static IServiceCollection AddMafIndexing(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMafRetrievalCore(configuration);
        services.Configure<IndexingOptions>(configuration.GetSection(IndexingOptions.Section));
        services.TryAddSingleton<ContextualEnricherFactory>();
        services.TryAddSingleton<IndexingPipeline>();
        services.TryAddSingleton<DriftService>();
        services.TryAddSingleton<MigrationService>();
        return services;
    }
}
