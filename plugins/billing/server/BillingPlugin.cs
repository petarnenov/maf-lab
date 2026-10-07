using Maf.Lab.Plugins.Abstractions;
using Maf.Lab.Retrieval.Store;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Plugins.Billing;

/// <summary>
/// The billing plugin's in-process part (extract-billing): the billing domain's behaviour — a routed read's arguments
/// from the question, the run status a mixed question needs, its tools' summaries — and its chunk store, which the
/// review queue resolves a billing search's sources in. Its descriptor is the manifest's [domain] table and its MCP
/// server the mcp-retrieval service; this part only adds what data cannot express.
/// </summary>
public sealed class BillingPlugin : IMafPlugin, IContributesDomainBehaviour, IContributesServices, IContributesWriteConfirmation
{
    public const string PluginName = "billing";

    /// <summary>The domain's id, as the manifest's [domain] table names it.</summary>
    public const string DomainId = "billing";

    public string Name => PluginName;

    public IDomainBehaviour Behaviour { get; } = new BillingBehaviour();

    // Billing's chunks are the core library's default collection (maf_chunks), so its store is the default one.
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddKeyedSingleton(DomainId, (sp, _) => sp.GetRequiredService<TenantScopedMaintenance>());
        FeeAdjustmentFlow.Configure(services);
    }

    /// <summary>A fee adjustment is confirmed by a person, after the reviewer when it is large (generalize-write-confirmation).</summary>
    public IWriteConfirmationFlow CreateFlow(IServiceProvider services) => FeeAdjustmentFlow.Create(services);
}
