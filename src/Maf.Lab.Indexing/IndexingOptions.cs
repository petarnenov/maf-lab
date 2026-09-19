namespace Maf.Lab.Indexing;

public sealed class IndexingOptions
{
    public const string Section = "Indexing";

    /// <summary>Corpus root laid out as {tenant}/{docs|procedures|code}/... . Empty = find "data/" upwards from the working directory.</summary>
    public string CorpusRoot { get; set; } = "";
    /// <summary>Contextual retrieval: prepend an LLM-generated situating sentence before dense embedding.</summary>
    public bool ContextualRetrieval { get; set; }
    /// <summary>Named dense vector the indexer writes; model_version is that vector's model.</summary>
    public string DenseVector { get; set; } = "dense_v1";
    public int MaxChunkChars { get; set; } = 1500;
    public int EmbeddingBatchSize { get; set; } = 32;
    public string CacheDirectory { get; set; } = ".cache";

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
                return candidate;
            }
        }
        return Path.GetFullPath("data");
    }
}
