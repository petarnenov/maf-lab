namespace Maf.Lab.Indexing;

public sealed class IndexingOptions
{
    public const string Section = "Indexing";

    /// <summary>Corpus root laid out as {tenant}/{docs|procedures|code}/... . Empty = find "data/" upwards from the working directory.</summary>
    public string CorpusRoot { get; set; } = "";
    /// <summary>Contextual retrieval: prepend an LLM-generated situating sentence before dense embedding.</summary>
    public bool ContextualRetrieval { get; set; }
    /// <summary>Named dense vector the indexer writes; model_version is that vector's model.</summary>
    public string DenseVector { get; set; } = "dense_v3";
    public int MaxChunkChars { get; set; } = 1500;
    /// <summary>
    /// When set, chunks are sized in embedding-model tokens (<see cref="Chunking.TokenEstimator"/>) instead of
    /// <see cref="MaxChunkChars"/>. The codebase corpus sets it; the billing and portfolio corpora keep characters.
    /// </summary>
    public int? MaxChunkTokens { get; set; }
    /// <summary>
    /// "tenants" — {tenant}/{docs|procedures|code}/... under the corpus root — or "repository": the repository itself,
    /// every file shared, filtered by <see cref="RepositoryInclude"/> and <see cref="RepositoryExclude"/>.
    /// </summary>
    public string Layout { get; set; } = CorpusLayouts.Tenants;
    public List<string> RepositoryInclude { get; set; } =
        ["src/", "web/src/", "tests/", "tools/", "scripts/", "plugins/", "openspec/specs/", "docs/", "README.md", "DECISIONS.md", "CLAUDE.md", "openspec/project.md"];
    public List<string> RepositoryExclude { get; set; } = [];
    /// <summary>Files larger than this are generated or data, not source, and are not indexed.</summary>
    public int RepositoryMaxFileBytes { get; set; } = 200_000;
    /// <summary>BM25 tokenizer the vocabulary is built with: "words" or "code" (identifiers also split into their parts).</summary>
    public string Bm25Tokenizer { get; set; } = Retrieval.Sparse.Bm25Tokenizers.Words;
    public int EmbeddingBatchSize { get; set; } = 32;
    public string CacheDirectory { get; set; } = ".cache";

    public bool RepositoryLayout => string.Equals(Layout, CorpusLayouts.Repository, StringComparison.OrdinalIgnoreCase);

    /// <summary>The budget every chunk of this corpus is cut to.</summary>
    public Chunking.ChunkBudget ChunkBudget => MaxChunkTokens is { } tokens ? Chunking.ChunkBudget.OfTokens(tokens) : MaxChunkChars;

    /// <summary>The corpus this configuration describes, in either layout.</summary>
    public Corpus.CorpusSnapshot LoadCorpus(IReadOnlySet<Domain.Tenancy.TenantId>? onlyTenants = null) =>
        RepositoryLayout
            ? Corpus.RepositoryCorpusLoader.Load(ResolveCorpusRoot(), RepositoryInclude, RepositoryExclude, RepositoryMaxFileBytes, onlyTenants)
            : Corpus.CorpusLoader.Load(ResolveCorpusRoot(), onlyTenants);

    public string ResolveCorpusRoot()
    {
        if (!string.IsNullOrWhiteSpace(CorpusRoot))
        {
            return Path.GetFullPath(CorpusRoot);
        }
        for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "data");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(dir.FullName, "maf-lab.sln")))
            {
                return RepositoryLayout ? dir.FullName : candidate;
            }
        }
        return Path.GetFullPath("data");
    }
}

public static class CorpusLayouts
{
    public const string Tenants = "tenants";
    public const string Repository = "repository";
}
