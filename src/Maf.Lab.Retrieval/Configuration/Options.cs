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

public sealed class AuthOptions
{
    public const string Section = "Auth";

    public string Issuer { get; set; } = "maf-lab-dev-issuer";
    public string Audience { get; set; } = "maf-lab";
    /// <summary>HMAC key, at least 32 bytes. Dev only; override via environment in compose.</summary>
    public string SigningKey { get; set; } = "maf-lab-dev-signing-key-change-me-0123456789";
    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromHours(8);
}
