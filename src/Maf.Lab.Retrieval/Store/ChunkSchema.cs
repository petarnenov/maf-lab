namespace Maf.Lab.Retrieval.Store;

/// <summary>Payload field and vector names of the chunk collection.</summary>
public static class ChunkSchema
{
    public const string TenantId = "tenant_id";
    public const string DocId = "doc_id";
    public const string ChunkId = "chunk_id";
    public const string SourceType = "source_type";
    public const string SourcePath = "source_path";
    public const string SectionPath = "section_path";
    public const string Symbol = "symbol";
    public const string UpdatedAt = "updated_at";
    /// <summary>The model of the indexing dense vector — what drift and status report.</summary>
    public const string ModelVersion = "model_version";
    /// <summary>
    /// Which model produced each dense vector of a point: <c>model_version__dense_v3</c> and so on. Flat keys, so a
    /// filter or a partial payload update names one vector's version without a nested path.
    /// </summary>
    public const string ModelVersionPrefix = "model_version__";
    public const string Text = "text";
    public const string Context = "context";
    public const string ContentHash = "content_hash";

    public const string SparseVector = "bm25";

    public static string ModelVersionOf(string denseVector) => ModelVersionPrefix + denseVector;
}

/// <summary>A chunk as written to and read from the store.</summary>
public sealed record ChunkRecord
{
    public required string TenantId { get; init; }
    public required string DocId { get; init; }
    public required string ChunkId { get; init; }
    public required string SourceType { get; init; }
    public required string SourcePath { get; init; }
    public required string SectionPath { get; init; }
    public string? Symbol { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public required string ModelVersion { get; init; }
    /// <summary>The model behind each dense vector the point carries, keyed by vector name.</summary>
    public IReadOnlyDictionary<string, string> DenseModelVersions { get; init; } = new Dictionary<string, string>();
    public required string Text { get; init; }
    public string? Context { get; init; }
    public required string ContentHash { get; init; }

    /// <summary>Deterministic point id so re-upserting the same chunk overwrites it.</summary>
    public Guid PointId => PointIds.FromChunkId(ChunkId);
}

public static class PointIds
{
    public static Guid FromChunkId(string chunkId)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(chunkId));
        return new Guid(hash.AsSpan(0, 16));
    }
}

public sealed record ScoredChunk(ChunkRecord Chunk, double Score);

/// <summary>Per-document view of what is in the index, used for drift and skip-unchanged.</summary>
public sealed record IndexedDocument(string DocId, string SourcePath, DateTimeOffset UpdatedAt, string ContentHash, string ModelVersion, int Chunks,
    IReadOnlyDictionary<string, string>? DenseModelVersions = null);
