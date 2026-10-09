using Maf.Lab.Retrieval.Configuration;
using Maf.Lab.Retrieval.Models;
using Maf.Lab.Retrieval.Rerank;
using Maf.Lab.Retrieval.Search;
using Maf.Lab.Retrieval.Sparse;
using Maf.Lab.Retrieval.Store;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Retrieval;

public static class RetrievalServiceCollectionExtensions
{
    /// <summary>Qdrant access, BM25, embeddings and document search. Shared by the MCP server, indexer, API admin and evals.</summary>
    public static IServiceCollection AddMafRetrievalCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<QdrantOptions>(configuration.GetSection(QdrantOptions.Section));
        services.Configure<ModelOptions>(configuration.GetSection(ModelOptions.Section));
        services.Configure<RetrievalOptions>(configuration.GetSection(RetrievalOptions.Section));

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(sp => QdrantFactory.Create(sp.GetRequiredService<IOptions<QdrantOptions>>()));
        services.TryAddSingleton<ModelProviders>();
        services.TryAddSingleton<IChatClientFactory>(sp => sp.GetRequiredService<ModelProviders>());
        services.TryAddSingleton<IDenseEncoder, DenseEncoder>();
        services.TryAddSingleton<CollectionBootstrapper>();
        services.TryAddSingleton<TenantScopedSearch>();
        services.TryAddSingleton<TenantScopedMaintenance>();
        services.TryAddSingleton<Bm25Store>();
        // The installed decision engine judges relevance for the gate and the relevance reranker; the engine itself is
        // registered by its provider plugin in the host (ProviderHost.AddInstalledProviders).
        services.TryAddSingleton<IRelevanceJudge, DecisionRelevanceJudge>();
        // Every reranker is registered; the search picks the one Retrieval:Reranker (or the eval) names.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IReranker, LlmReranker>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IReranker, RelevanceReranker>());
        services.TryAddSingleton<IQueryTranslator>(sp =>
            sp.GetRequiredService<IOptions<RetrievalOptions>>().Value.NormalizeQueryLanguage
                ? ActivatorUtilities.CreateInstance<LlmQueryTranslator>(sp)
                : new NoOpQueryTranslator());
        services.TryAddSingleton<DocumentSearchService>();
        return services;
    }
}
