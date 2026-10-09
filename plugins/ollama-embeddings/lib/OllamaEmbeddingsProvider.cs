using System.ClientModel;
using Maf.Lab.Retrieval.Configuration;
using Maf.Lab.Retrieval.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OllamaSharp;
using OllamaSharp.Models;
using OllamaSharp.Models.Exceptions;
using OpenAI;

namespace Maf.Lab.Plugins.OllamaEmbeddings;

/// <summary>Creates an embedder per profile and purpose; each instance's wire options stay inside the provider.</summary>
public sealed class OllamaEmbeddingsProvider(IOptions<ModelOptions> options) : IEmbeddingsProvider
{
    private readonly ModelOptions _options = options.Value;
    private readonly Func<HttpMessageHandler>? _handler;

    internal OllamaEmbeddingsProvider(IOptions<ModelOptions> options, Func<HttpMessageHandler> handler) : this(options) => _handler = handler;

    public IEmbeddingGenerator<string, Embedding<float>> CreateGenerator(EmbeddingProfile profile, EmbeddingRole role)
    {
        // Retain the pre-plugin Models:Provider=openai deployments until a dedicated provider replaces this option.
        if (!string.Equals(_options.Provider, "ollama", StringComparison.OrdinalIgnoreCase))
        {
            var key = _options.OpenAIApiKey ?? throw new InvalidOperationException("Models:OpenAIApiKey is required for the openai provider.");
            var settings = new OpenAIClientOptions();
            if (!string.IsNullOrWhiteSpace(_options.OpenAIEndpoint))
            {
                settings.Endpoint = new Uri(_options.OpenAIEndpoint);
            }
            return new OpenAIClient(new ApiKeyCredential(key), settings).GetEmbeddingClient(profile.Model).AsIEmbeddingGenerator();
        }
        var batch = role == EmbeddingRole.Documents && !string.IsNullOrWhiteSpace(_options.BatchOllamaEndpoint);
        var endpoint = batch ? _options.BatchOllamaEndpoint! : _options.OllamaEndpoint;
        var threads = batch ? _options.BatchOllamaNumThread ?? 12 : _options.OllamaNumThread ?? 4;
        var http = _handler is null ? new HttpClient() : new HttpClient(_handler());
        http.BaseAddress = new Uri(endpoint);
        if (_options.EmbeddingTimeoutSeconds is { } seconds)
        {
            http.Timeout = TimeSpan.FromSeconds(seconds);
        }
        return new InstanceEmbeddings(new OllamaApiClient(http, profile.Model), http, profile, role, threads);
    }

    /// <summary>MEAI Adapter: every call carries num_thread, and document batches refuse silent truncation.</summary>
    private sealed class InstanceEmbeddings(OllamaApiClient client, HttpClient http, EmbeddingProfile profile, EmbeddingRole role,
        int threads) : IEmbeddingGenerator<string, Embedding<float>>
    {
        public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
        {
            var refuseTruncation = role == EmbeddingRole.Documents && profile.MaxInputTokens is not null;
            var request = new EmbedRequest
            {
                Model = options?.ModelId ?? profile.Model,
                Input = values.ToList(),
                Options = new RequestOptions { NumThread = threads },
                Truncate = refuseTruncation ? false : null,
            };
            try
            {
                var response = await client.EmbedAsync(request, cancellationToken);
                return new GeneratedEmbeddings<Embedding<float>>(response.Embeddings.Select(v => new Embedding<float>(v)
                {
                    ModelId = request.Model,
                })) { Usage = new UsageDetails { InputTokenCount = response.PromptEvalCount } };
            }
            catch (OllamaException ex) when (refuseTruncation && ex.Message.Contains("context length", StringComparison.OrdinalIgnoreCase))
            {
                throw new InputTooLongException(profile.Model, profile.MaxInputTokens!.Value, ex);
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceKey is null && serviceType == typeof(EmbeddingGeneratorMetadata)
                ? new EmbeddingGeneratorMetadata("ollama", http.BaseAddress, profile.Model, profile.Dimensions)
                : serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
            client.Dispose();
            http.Dispose();
        }
    }
}
