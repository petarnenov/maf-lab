using Maf.Lab.Domain.Portfolio;
using Maf.Lab.Plugins.Abstractions;
using Maf.Lab.Retrieval.Store;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Plugins.Portfolio;

/// <summary>
/// The portfolio plugin's in-process part (extract-portfolio): the portfolio domain's behaviour — a routed read's
/// account, the account in focus, its tools' summaries — and its chunk store, which the review queue resolves a portfolio
/// search's sources in. Its descriptor is the manifest's [domain] table and its MCP server the mcp-portfolio service;
/// this part only adds what data cannot express.
/// </summary>
public sealed class PortfolioPlugin : IMafPlugin, IContributesDomainBehaviour, IContributesServices
{
    public const string PluginName = "portfolio";

    /// <summary>The domain's id, as the manifest's [domain] table names it.</summary>
    public const string DomainId = "portfolio";

    public string Name => PluginName;

    public IDomainBehaviour Behaviour { get; } = new PortfolioBehaviour();

    // Portfolio's chunks are in a collection of their own, on the core's Qdrant connection.
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration) =>
        services.AddKeyedSingleton(DomainId, (sp, _) => DomainChunkStore.For(sp,
            configuration["Portfolio:Collection"] ?? PortfolioCollections.Chunks,
            configuration["Portfolio:MetaCollection"] ?? PortfolioCollections.Meta));
}
