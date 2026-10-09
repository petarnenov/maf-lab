using Maf.Lab.Domain.Services;
using Maf.Lab.Hosting.Services;
using Maf.Lab.Retrieval.Graph;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Maf.Lab.Plugins.Topology;

public sealed class TopologyPlugin : IMafPlugin, IContributesServices, IContributesEndpoints
{
    public const string PluginName = "topology";
    public string Name => PluginName;
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TopologyOptions>(configuration.GetSection(TopologyOptions.Section));
        services.AddMemoryCache();
        services.AddHttpClient("topology");
        services.TryAddSingleton<IServiceResolver, DnsServiceResolver>();
        services.AddGraphStore(configuration);
        services.AddSingleton<TopologyProbe>();
    }
    public void MapEndpoints(IMafEndpoints endpoints) => endpoints.Routes.MapTopology();
}
