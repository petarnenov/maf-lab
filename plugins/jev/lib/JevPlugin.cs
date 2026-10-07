using Maf.Lab.Plugins.Abstractions;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Maf.Lab.Plugins.Jev;

/// <summary>
/// The jev provider plugin (introduce-provider-plugins 5t): registers TypeSafe Jev as the process's
/// <see cref="IDecisionEngine"/> — the client, its credential (JEV_MAF_LAB, bearer header only), retry, circuit breaker
/// and warm-up — in every host that takes providers.
/// </summary>
public sealed class JevPlugin : IMafPlugin, IContributesProvider
{
    public const string PluginName = "jev";

    public string Name => PluginName;

    public string Provides => ProviderKinds.DecisionEngine;

    public void ConfigureProvider(IServiceCollection services, IConfiguration configuration)
    {
        services.AddJevClient(configuration);
        services.TryAddSingleton<IDecisionEngine, JevDecisionEngine>();
    }
}
