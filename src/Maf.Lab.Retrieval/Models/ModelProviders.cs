using System.ClientModel;
using Maf.Lab.Retrieval.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OllamaSharp;
using OpenAI;

namespace Maf.Lab.Retrieval.Models;

/// <summary>Creates chat clients; the seam tests and evals use to substitute a scripted model.</summary>
public interface IChatClientFactory
{
    IChatClient CreateChatClient(string? model = null);
    ChatOptions BaseChatOptions();
}

/// <summary>
/// Builds chat and embedding clients from configuration so the provider can be switched
/// (Ollama locally, OpenAI / Azure OpenAI v1 endpoint) without code changes.
/// </summary>
public sealed class ModelProviders(IOptions<ModelOptions> options) : IChatClientFactory
{
    private readonly ModelOptions _options = options.Value;
    private readonly Dictionary<string, IEmbeddingGenerator<string, Embedding<float>>> _embedders = new();
    private readonly Lock _gate = new();
    private HttpClient? _chatHttp;

    public IChatClient CreateChatClient(string? model = null)
    {
        model ??= _options.ChatModel;
        IChatClient client = IsOllama
            ? new OllamaApiClient(ChatHttpClient(), model)
            : OpenAIClient().GetChatClient(model).AsIChatClient();
        return client;
    }

    /// <summary>
    /// One shared HttpClient for chat. When the chat endpoint is remote (Ollama Cloud) the bearer key is read from
    /// the configured environment variable; a missing key fails fast with the variable's name (never its value).
    /// </summary>
    private HttpClient ChatHttpClient()
    {
        lock (_gate)
        {
            if (_chatHttp is not null)
            {
                return _chatHttp;
            }
            var endpoint = new Uri(string.IsNullOrWhiteSpace(_options.ChatEndpoint) ? _options.OllamaEndpoint : _options.ChatEndpoint);
            var http = new HttpClient { BaseAddress = endpoint, Timeout = TimeSpan.FromMinutes(5) };
            var key = Environment.GetEnvironmentVariable(_options.ChatApiKeyEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(key))
            {
                http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
            }
            else if (!endpoint.IsLoopback && endpoint.Host is not ("ollama" or "host.docker.internal"))
            {
                throw new InvalidOperationException(
                    $"Chat endpoint {endpoint.Host} needs an API key: set the {_options.ChatApiKeyEnvironmentVariable} environment variable.");
            }
            return _chatHttp = http;
        }
    }

    /// <summary>Chat options every call should start from (e.g. thinking disabled for reasoning models).</summary>
    public ChatOptions BaseChatOptions()
    {
        var chatOptions = new ChatOptions { Temperature = 0 };
        if (IsOllama && _options.DisableThinking)
        {
            chatOptions.RawRepresentationFactory = _ => new OllamaSharp.Models.Chat.ChatRequest { Think = false };
        }
        return chatOptions;
    }

    public IEmbeddingGenerator<string, Embedding<float>> GetEmbedder(string vectorName)
    {
        var profile = GetProfile(vectorName);
        lock (_gate)
        {
            if (!_embedders.TryGetValue(vectorName, out var embedder))
            {
                embedder = IsOllama
                    ? new OllamaApiClient(new Uri(_options.OllamaEndpoint), profile.Model)
                    : OpenAIClient().GetEmbeddingClient(profile.Model).AsIEmbeddingGenerator();
                _embedders[vectorName] = embedder;
            }
            return embedder;
        }
    }

    public EmbeddingProfile GetProfile(string vectorName) =>
        _options.Embeddings.TryGetValue(vectorName, out var profile)
            ? profile
            : throw new InvalidOperationException($"No embedding profile configured for vector '{vectorName}'.");

    public IReadOnlyDictionary<string, EmbeddingProfile> Profiles => _options.Embeddings;

    private bool IsOllama => string.Equals(_options.Provider, "ollama", StringComparison.OrdinalIgnoreCase);

    private OpenAIClient OpenAIClient()
    {
        var key = _options.OpenAIApiKey ?? throw new InvalidOperationException("Models:OpenAIApiKey is required for the openai provider.");
        var clientOptions = new OpenAIClientOptions();
        if (!string.IsNullOrWhiteSpace(_options.OpenAIEndpoint))
        {
            clientOptions.Endpoint = new Uri(_options.OpenAIEndpoint);
        }
        return new OpenAIClient(new ApiKeyCredential(key), clientOptions);
    }
}

/// <summary>Embeds text for one named dense vector, applying the model's document/query prefixes.</summary>
public interface IDenseEncoder
{
    Task<float[]> EmbedQueryAsync(string vectorName, string text, CancellationToken ct);
    Task<IReadOnlyList<float[]>> EmbedDocumentsAsync(string vectorName, IReadOnlyList<string> texts, CancellationToken ct);
    string ModelVersion(string vectorName);
}

public sealed class DenseEncoder(ModelProviders providers) : IDenseEncoder
{
    public async Task<float[]> EmbedQueryAsync(string vectorName, string text, CancellationToken ct)
    {
        var profile = providers.GetProfile(vectorName);
        var result = await providers.GetEmbedder(vectorName).GenerateAsync([profile.QueryPrefix + text], cancellationToken: ct);
        return result[0].Vector.ToArray();
    }

    public async Task<IReadOnlyList<float[]>> EmbedDocumentsAsync(string vectorName, IReadOnlyList<string> texts, CancellationToken ct)
    {
        var profile = providers.GetProfile(vectorName);
        var embedder = providers.GetEmbedder(vectorName);
        var inputs = texts.Select(t => profile.DocumentPrefix + t).ToList();
        // Ollama cuts a document past the context window without a word, and the index would then hold a vector for
        // text it never read. With a known window the request asks Ollama to refuse instead, so a chunk the indexer
        // sized wrong fails the run. Microsoft.Extensions.AI has no option for it (OllamaSharp ignores the raw request
        // factory for embeddings), hence the native call. Queries keep the default: a cut query still searches.
        if (profile.MaxInputTokens is not null && embedder is OllamaSharp.IOllamaApiClient ollama)
        {
            try
            {
                var response = await ollama.EmbedAsync(new OllamaSharp.Models.EmbedRequest { Model = profile.Model, Input = inputs, Truncate = false }, ct);
                return response.Embeddings.ToArray();
            }
            catch (OllamaSharp.Models.Exceptions.OllamaException ex) when (ex.Message.Contains("context length", StringComparison.OrdinalIgnoreCase))
            {
                throw new InputTooLongException(profile.Model, profile.MaxInputTokens.Value, ex);
            }
        }
        var result = await embedder.GenerateAsync(inputs, cancellationToken: ct);
        return result.Select(e => e.Vector.ToArray()).ToArray();
    }

    public string ModelVersion(string vectorName) => providers.GetProfile(vectorName).Model;
}

/// <summary>A document longer than the embedding model's context window, which the provider refused to cut.</summary>
public sealed class InputTooLongException(string model, int maxInputTokens, Exception inner)
    : Exception($"A document exceeds {model}'s context window of {maxInputTokens} tokens; the chunker must split it smaller.", inner)
{
    public int MaxInputTokens { get; } = maxInputTokens;
}
