using System.ClientModel;
using Maf.Lab.Retrieval.Configuration;
using Maf.Lab.Retrieval.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OllamaSharp;
using OpenAI;

namespace Maf.Lab.Plugins.OllamaCloud;

public sealed class OllamaCloudProvider(IOptions<ModelOptions> options) : IChatModelProvider, IDisposable
{
    private readonly ModelOptions _options = options.Value;
    private readonly Lock _gate = new();
    private HttpClient? _chatHttp;
    private readonly Func<HttpMessageHandler>? _handler;
    public string Name => OllamaCloudPlugin.PluginName;

    internal OllamaCloudProvider(IOptions<ModelOptions> options, Func<HttpMessageHandler> handler) : this(options) => _handler = handler;

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
            var key = Environment.GetEnvironmentVariable(_options.ChatApiKeyEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(key) && !endpoint.IsLoopback && endpoint.Host is not ("ollama" or "host.docker.internal"))
            {
                throw new InvalidOperationException(
                    $"Chat endpoint {endpoint.Host} needs an API key: set the {_options.ChatApiKeyEnvironmentVariable} environment variable.");
            }
            var http = _handler is null ? new HttpClient() : new HttpClient(_handler());
            http.BaseAddress = endpoint;
            http.Timeout = TimeSpan.FromMinutes(5);
            if (!string.IsNullOrWhiteSpace(key))
            {
                http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
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
    public void Dispose() => _chatHttp?.Dispose();
}
