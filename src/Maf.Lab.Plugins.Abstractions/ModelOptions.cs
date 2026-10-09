namespace Maf.Lab.Retrieval.Configuration;

public sealed class ModelOptions
{
    public const string Section = "Models";

    /// <summary>"ollama" or "openai" (OpenAI, or Azure OpenAI via its v1 endpoint).</summary>
    public string Provider { get; set; } = "ollama";
    /// <summary>Local Ollama for embeddings. With <see cref="BatchOllamaEndpoint"/> set, it serves search queries only.</summary>
    public string OllamaEndpoint { get; set; } = "http://localhost:11434";
    /// <summary>
    /// A second local Ollama, serving the same embedding model, for document embeddings (indexing, migration, admin
    /// index runs), so a batch never queues in front of a search. Unset: documents use <see cref="OllamaEndpoint"/>.
    /// </summary>
    public string? BatchOllamaEndpoint { get; set; }
    /// <summary>
    /// Threads per request on <see cref="OllamaEndpoint"/>, matching the CPUs its container is pinned to. Ollama does
    /// not derive it from the container, and a request with another value (or none) reloads the model. Unset: dev default 4.
    /// </summary>
    public int? OllamaNumThread { get; set; }
    /// <summary>Threads per request on <see cref="BatchOllamaEndpoint"/>; unset: dev default 12.</summary>
    public int? BatchOllamaNumThread { get; set; }
    public string? OpenAIEndpoint { get; set; }
    public string? OpenAIApiKey { get; set; }
    /// <summary>
    /// Ollama endpoint for chat (agent, judge, rerank, contextual). Default: Ollama Cloud. Embeddings always use the
    /// local <see cref="OllamaEndpoint"/> / <see cref="BatchOllamaEndpoint"/>, since Ollama Cloud serves no embedding models.
    /// </summary>
    public string? ChatEndpoint { get; set; } = "https://ollama.com";
    /// <summary>Environment variable holding the Ollama Cloud API key. The key itself is never stored in config files.</summary>
    public string ChatApiKeyEnvironmentVariable { get; set; } = "OLLAMA_API_KEY";
    public string ChatModel { get; set; } = "gpt-oss:120b";
    /// <summary>Model that brings a query into the corpus language; empty uses the chat model.</summary>
    public string? TranslationModel { get; set; }
    /// <summary>Disables reasoning tokens for models that support it (qwen3 etc.).</summary>
    public bool DisableThinking { get; set; } = true;
    public string? RerankModel { get; set; }
    /// <summary>
    /// How long one embedding request may take. Null keeps HttpClient's 100 s, which a search's single query never
    /// nears; the indexer raises it (<c>AddMafIndexing</c>), since a batch of long chunks on CPU Ollama can take longer
    /// and a timeout cancels the whole run.
    /// </summary>
    public int? EmbeddingTimeoutSeconds { get; set; }

    /// <summary>
    /// Dense embedding profiles keyed by Qdrant named-vector name. One, by decision (adopt-multilingual-embedding): the
    /// multilingual model that won the bake-off against bge-m3 on worst-language hybrid recall. To change or roll back
    /// the model, replace this profile (with its own calibrated floor), select it, and `make rebuild-index FORCE=1` —
    /// Qdrant cannot add a named vector to an existing collection.
    /// </summary>
    public Dictionary<string, EmbeddingProfile> Embeddings { get; set; } = new()
    {
        ["dense_v3"] = new EmbeddingProfile
        {
            Model = "embeddinggemma",
            Dimensions = 768,
            DocumentPrefix = "title: none | text: ",
            QueryPrefix = "task: search result | query: ",
            // From a sweep of 0.20–0.24 against the retrieval eval: 0.22 silences 2 of 6 off-domain questions (none
            // without a floor) with recall@5 up in every language and recall@20 down 0.012, inside the suite's noise.
            // nomic-embed-text's floor was 0.65; this model scores the same closeness far lower.
            DenseFloor = 0.22f,
            // gemma3.context_length as Ollama reports it (api/show); longer input is cut, or refused with truncate=false.
            MaxInputTokens = 2048,
        },
    };
}

public sealed class EmbeddingProfile
{
    public string Model { get; set; } = "";
    public int Dimensions { get; set; }
    public string DocumentPrefix { get; set; } = "";
    public string QueryPrefix { get; set; } = "";
    /// <summary>
    /// Lowest dense score a candidate may have and still reach fusion, for this model. It belongs here and not to
    /// retrieval as a whole: models place the same closeness at different scores (nomic's answerable questions score
    /// a median 0.59, embeddinggemma's 0.50), so a floor calibrated for one silences or admits everything under another.
    /// Null means this model has no floor.
    /// </summary>
    public float? DenseFloor { get; set; }

    /// <summary>
    /// The model's context window in its own tokens, document prefix included. Input past it is not embedded: Ollama
    /// cuts it silently by default, so the indexer sizes chunks to stay under it and asks the provider to refuse
    /// rather than cut (add-codebase-search). Null means unknown: no ceiling is enforced.
    /// </summary>
    public int? MaxInputTokens { get; set; }
}
