using Maf.Lab.Plugins.Abstractions;
using Maf.Lab.Retrieval.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Retrieval.Models;

/// <summary>Selects the installed providers; transport and vendor options live in their plugins.</summary>
public sealed class ModelProviders(IOptions<ModelOptions> options, IEnumerable<IChatModelProvider> chatProviders,
    IEnumerable<IEmbeddingsProvider> embeddings, IOptions<InstalledProviders> installed) : IChatClientFactory, IDisposable
{
    private readonly ModelOptions _options = options.Value;
    private readonly Dictionary<(string Vector, EmbeddingRole Role), IEmbeddingGenerator<string, Embedding<float>>> _embedders = new();
    private readonly Lock _gate = new();

    private IChatModelProvider Chat => chatProviders.SingleOrDefault(p => p.Name == installed.Value.ChatModel)
        ?? throw new InvalidOperationException($"chat provider '{installed.Value.ChatModel}' is not registered (MAF_CHAT_MODEL)");

    public IChatClient CreateChatClient(string? model = null) => Chat.CreateChatClient(model);

    public ChatOptions BaseChatOptions() => Chat.BaseChatOptions();

    public IEmbeddingGenerator<string, Embedding<float>> GetEmbedder(string vectorName, EmbeddingRole role = EmbeddingRole.Query)
    {
        lock (_gate)
        {
            if (!_embedders.TryGetValue((vectorName, role), out var embedder))
            {
                var provider = embeddings.SingleOrDefault()
                    ?? throw new InvalidOperationException("no embeddings provider is registered: install one provider with provides = \"embeddings\"");
                embedder = provider.CreateGenerator(GetProfile(vectorName), role);
                _embedders[(vectorName, role)] = embedder;
            }
            return embedder;
        }
    }

    public EmbeddingProfile GetProfile(string vectorName) =>
        _options.Embeddings.TryGetValue(vectorName, out var profile)
            ? profile
            : throw new InvalidOperationException($"No embedding profile configured for vector '{vectorName}'.");

    public IReadOnlyDictionary<string, EmbeddingProfile> Profiles => _options.Embeddings;

    public void Dispose()
    {
        foreach (var embedder in _embedders.Values)
        {
            embedder.Dispose();
        }
    }
}

/// <summary>Applies profile prefixes; the provider owns request options and refusal of overlong documents.</summary>
public sealed class DenseEncoder(ModelProviders providers) : IDenseEncoder
{
    public async Task<float[]> EmbedQueryAsync(string vectorName, string text, CancellationToken ct)
    {
        var profile = providers.GetProfile(vectorName);
        var result = await providers.GetEmbedder(vectorName, EmbeddingRole.Query)
            .GenerateAsync([profile.QueryPrefix + text], cancellationToken: ct);
        return result[0].Vector.ToArray();
    }

    public async Task<IReadOnlyList<float[]>> EmbedDocumentsAsync(string vectorName, IReadOnlyList<string> texts, CancellationToken ct)
    {
        var profile = providers.GetProfile(vectorName);
        var result = await providers.GetEmbedder(vectorName, EmbeddingRole.Documents)
            .GenerateAsync(texts.Select(t => profile.DocumentPrefix + t), cancellationToken: ct);
        return result.Select(e => e.Vector.ToArray()).ToArray();
    }

    public string ModelVersion(string vectorName) => providers.GetProfile(vectorName).Model;
}
