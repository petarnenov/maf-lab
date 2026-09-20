namespace Maf.Lab.Retrieval.Configuration;

public sealed class QdrantOptions
{
    public const string Section = "Qdrant";

    public string Host { get; set; } = "localhost";
    public int GrpcPort { get; set; } = 6334;
    public bool Https { get; set; }
    public string? ApiKey { get; set; }
    public string Collection { get; set; } = "maf_chunks";
    public string MetaCollection { get; set; } = "maf_meta";
    /// <summary>payload_m for per-tenant HNSW graphs (global m is 0).</summary>
    public ulong PayloadM { get; set; } = 16;
}

public sealed class ModelOptions
{
    public const string Section = "Models";

    /// <summary>"ollama" or "openai" (OpenAI, or Azure OpenAI via its v1 endpoint).</summary>
    public string Provider { get; set; } = "ollama";
    public string OllamaEndpoint { get; set; } = "http://localhost:11434";
    public string? OpenAIEndpoint { get; set; }
    public string? OpenAIApiKey { get; set; }
    /// <summary>
    /// Ollama endpoint for chat (agent, judge, rerank, contextual). Default: Ollama Cloud. Embeddings always use
    /// <see cref="OllamaEndpoint"/> (local), since Ollama Cloud serves no embedding models.
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

    /// <summary>Dense embedding profiles keyed by Qdrant named-vector name.</summary>
    public Dictionary<string, EmbeddingProfile> Embeddings { get; set; } = new()
    {
        ["dense_v1"] = new EmbeddingProfile
        {
            Model = "nomic-embed-text",
            Dimensions = 768,
            DocumentPrefix = "search_document: ",
            QueryPrefix = "search_query: ",
        },
        // Provisioned from the start so migration only fills vectors (Qdrant cannot add a named vector to an existing collection).
        ["dense_v2"] = new EmbeddingProfile
        {
            Model = "all-minilm",
            Dimensions = 384,
        },
    };
}

public sealed class EmbeddingProfile
{
    public string Model { get; set; } = "";
    public int Dimensions { get; set; }
    public string DocumentPrefix { get; set; } = "";
    public string QueryPrefix { get; set; } = "";
}

public sealed class RetrievalOptions
{
    public const string Section = "Retrieval";

    /// <summary>hybrid | dense | sparse.</summary>
    public string Mode { get; set; } = RetrievalModes.Hybrid;
    /// <summary>rrf | dbsf.</summary>
    public string Fusion { get; set; } = FusionModes.Rrf;
    /// <summary>Named dense vector used by queries.</summary>
    public string DenseVector { get; set; } = "dense_v1";
    public int PrefetchMultiplier { get; set; } = 5;
    public int MinPrefetch { get; set; } = 50;
    public bool RerankEnabled { get; set; }
    public int RerankCandidates { get; set; } = 20;
    public int SnippetMaxChars { get; set; } = 700;
    /// <summary>For traced searches, also run dense-only and sparse-only queries so the monitor can compare branches.</summary>
    public bool TraceBranches { get; set; } = true;
    /// <summary>Translate a query written in another language into the corpus language before searching.</summary>
    public bool NormalizeQueryLanguage { get; set; } = true;
    /// <summary>Language the indexed documents are written in.</summary>
    public string CorpusLanguage { get; set; } = "en";
    public double TranslationTimeoutSeconds { get; set; } = 5;
    public int TranslationCacheSize { get; set; } = 500;
}

public static class RetrievalModes
{
    public const string Hybrid = "hybrid";
    public const string Dense = "dense";
    public const string Sparse = "sparse";

    public static readonly IReadOnlyList<string> All = [Hybrid, Dense, Sparse];
}

public static class FusionModes
{
    public const string Rrf = "rrf";
    public const string Dbsf = "dbsf";
}
