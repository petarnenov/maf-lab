using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Neo4j.Driver;

namespace Maf.Lab.Retrieval.Graph;

public static class GraphServiceCollectionExtensions
{
    /// <summary>
    /// The graph store: one driver, the one tenant-scoped read path and the one maintenance path. Creating the driver
    /// does not connect, so a service that registers this still starts while the graph store is down.
    /// </summary>
    public static IServiceCollection AddGraphStore(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<GraphOptions>(configuration.GetSection(GraphOptions.Section));
        services.TryAddSingleton<IDriver>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<GraphOptions>>().Value;
            return GraphDatabase.Driver(options.Uri, AuthTokens.Basic(options.User, options.Password), o => o
                .WithConnectionTimeout(TimeSpan.FromSeconds(options.ConnectTimeoutSeconds))
                .WithMaxTransactionRetryTime(TimeSpan.FromSeconds(options.ConnectTimeoutSeconds)));
        });
        services.TryAddSingleton<TenantScopedGraph>();
        services.TryAddSingleton<IGraphReader>(sp => sp.GetRequiredService<TenantScopedGraph>());
        services.TryAddSingleton<TenantScopedGraphMaintenance>();
        return services;
    }
}
