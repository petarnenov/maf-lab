using Maf.Lab.Plugins.Abstractions;
using Maf.Lab.Retrieval.Configuration;
using Maf.Lab.Retrieval.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Maf.Lab.Plugins.OllamaCloud;

public sealed class OllamaCloudPlugin : IMafPlugin, IContributesProvider
{
    public const string PluginName = "ollama-cloud";
    public string Name => PluginName;
    public string Provides => ProviderKinds.ChatModel;

    public void ConfigureProvider(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ModelOptions>(configuration.GetSection(ModelOptions.Section));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IChatModelProvider, OllamaCloudProvider>());
    }
}
