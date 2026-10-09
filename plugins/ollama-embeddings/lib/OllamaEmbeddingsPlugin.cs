using Maf.Lab.Plugins.Abstractions;
using Maf.Lab.Retrieval.Configuration;
using Maf.Lab.Retrieval.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Maf.Lab.Plugins.OllamaEmbeddings;

public sealed class OllamaEmbeddingsPlugin : IMafPlugin, IContributesProvider
{
    public const string PluginName = "ollama-embeddings";
    public string Name => PluginName;
    public string Provides => ProviderKinds.Embeddings;

    public void ConfigureProvider(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ModelOptions>(configuration.GetSection(ModelOptions.Section));
        services.TryAddSingleton<IEmbeddingsProvider, OllamaEmbeddingsProvider>();
    }
}
