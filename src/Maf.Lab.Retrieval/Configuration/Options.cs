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

public sealed class RetrievalOptions
{
    public const string Section = "Retrieval";

    /// <summary>hybrid | dense | sparse.</summary>
    public string Mode { get; set; } = RetrievalModes.Hybrid;
    /// <summary>rrf | dbsf.</summary>
    public string Fusion { get; set; } = FusionModes.Rrf;
    /// <summary>Named dense vector used by queries.</summary>
    public string DenseVector { get; set; } = "dense_v3";
    public int PrefetchMultiplier { get; set; } = 5;
    public int MinPrefetch { get; set; } = 50;
    /// <summary>
    /// Overrides the selected embedding's own dense floor (<see cref="EmbeddingProfile.DenseFloor"/>). Null — the
    /// default — uses the floor calibrated for whichever dense vector is selected, so switching the vector switches
    /// the floor. Dense and sparse are different scales and never share a value.
    /// </summary>
    public float? DenseFloor { get; set; }
    /// <summary>False switches the dense floor off altogether: the nearest candidates, whatever their scores.</summary>
    public bool DenseFloorEnabled { get; set; } = true;

    /// <summary>The dense floor that applies to <paramref name="denseVector"/>: the override, else that embedding's own.</summary>
    public float? DenseFloorFor(ModelOptions models, string denseVector) =>
        !DenseFloorEnabled ? null : DenseFloor ?? models.Embeddings.GetValueOrDefault(denseVector)?.DenseFloor;
    /// <summary>
    /// Lowest BM25 score a sparse candidate may have and still reach fusion. Null: deliberately no floor.
    /// A sweep of 1–20 found that the value which silences the remaining off-domain questions (9) costs
    /// Bulgarian recall@5 0.042, well outside the 0.02 regression tolerance, because BM25 scales with term
    /// rarity and query length rather than with closeness to the query. There is no value that buys the silence
    /// without the loss, so this branch keeps no floor until something other than a raw BM25 score can judge it.
    /// </summary>
    public float? SparseFloor { get; set; }

    /// <summary>
    /// Asks Jev, once per search, whether each of the first fused candidates addresses the subject of the query, and
    /// returns nothing when none reaches <see cref="RelevanceFloor"/> — the judge the sparse floor above was waiting
    /// for. It decides whether the corpus answers, never which chunks: a search that passes is returned as fused.
    /// On by decision (add-jev-passage-relevance): four runs of the retrieval eval, off-domain silence 0.33 → 1.0 and
    /// recall@5 in every language identical to the ungated search of the same run.
    /// </summary>
    public bool RelevanceGateEnabled { get; set; } = true;

    /// <summary>
    /// Lowest top relevance probability a search may have and still answer. On the production hybrid shortlists (four
    /// eval runs) off-domain maxima were 0.03–0.08 and the lowest in-domain maximum 0.54; 0.3 sits in that gap.
    /// </summary>
    public double RelevanceFloor { get; set; } = 0.3;

    /// <summary>How many of the first fused candidates Jev judges (one Noul each, one request).</summary>
    public int RelevanceCandidates { get; set; } = 20;

    /// <summary>Budget for the relevance request; past it the search proceeds as if the gate were off.</summary>
    public double RelevanceTimeoutSeconds { get; set; } = 2;

    /// <summary>Characters of each passage Jev reads (after its section path), as the LLM reranker does.</summary>
    public int RelevancePassageChars { get; set; } = 400;

    /// <summary>
    /// On by decision (add-jev-passage-relevance), with Jev: three runs, recall@5 0.696–0.703 → 0.772–0.779 and MRR
    /// 0.619–0.627 → 0.763–0.790 — about ten times the run-to-run spread — at no extra request, since the gate already asks.
    /// </summary>
    public bool RerankEnabled { get; set; } = true;

    /// <summary><c>jev</c> (Jev's relevance probabilities, the gate's own request) or <c>llm</c> (listwise, the chat model, ~2.8 s).</summary>
    public string Reranker { get; set; } = RerankerKinds.Jev;
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

public static class RerankerKinds
{
    public const string Llm = "llm";
    public const string Jev = "jev";
}

public static class FusionModes
{
    public const string Rrf = "rrf";
    public const string Dbsf = "dbsf";
}
