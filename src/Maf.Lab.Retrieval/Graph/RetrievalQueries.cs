using Maf.Lab.Retrieval.Sparse;
using Maf.Lab.Retrieval.Store;

namespace Maf.Lab.Retrieval.Graph;

/// <summary>A chunk the retrieval spike found, with the score the template returned (not yet converted).</summary>
public sealed record GraphChunkHit(ChunkRecord Chunk, double Score);

/// <summary>Nearest copied chunks of one collection by the dense vector, tenant applied inside the index (neo4j-retrieval-spike).</summary>
public sealed record ChunkDenseSearch(string Collection, float[] Vector, IReadOnlyList<string>? SourceTypes, int Count) : GraphQuery<IReadOnlyList<GraphChunkHit>>
{
    public override string Name => SourceTypes is { Count: > 0 } ? "retrieval_dense_typed" : "retrieval_dense";
    public override int Limit => Count;
    internal override string Cypher => SourceTypes is { Count: > 0 } ? GraphTemplates.RetrievalDenseTyped : GraphTemplates.RetrievalDense;
    internal override IReadOnlyDictionary<string, object?> Arguments => new Dictionary<string, object?>
    {
        ["collection"] = Collection,
        // The driver has no single-precision list.
        ["vector"] = Vector.Select(x => (double)x).ToList(),
        ["types"] = SourceTypes?.ToList() ?? [],
    };

    internal override IReadOnlyList<GraphChunkHit> Map(IReadOnlyList<IGraphRow> rows, bool truncated) => RetrievalRows.Map(rows);
}

/// <summary>Copied chunks of one collection by BM25 over the graph's inverted index (neo4j-retrieval-spike).</summary>
public sealed record ChunkSparseSearch(string Collection, SparseVectorData Query, IReadOnlyList<string>? SourceTypes, int Count) : GraphQuery<IReadOnlyList<GraphChunkHit>>
{
    public override string Name => SourceTypes is { Count: > 0 } ? "retrieval_sparse_typed" : "retrieval_sparse";
    public override int Limit => Count;
    internal override string Cypher => SourceTypes is { Count: > 0 } ? GraphTemplates.RetrievalSparseTyped : GraphTemplates.RetrievalSparse;
    internal override IReadOnlyDictionary<string, object?> Arguments => new Dictionary<string, object?>
    {
        ["collection"] = Collection,
        ["terms"] = Query.Indices.Select((id, i) => (object)new Dictionary<string, object?>
        {
            ["key"] = RetrievalGraph.TermKey(Collection, id),
            ["w"] = (double)Query.Values[i],
        }).ToList(),
        ["types"] = SourceTypes?.ToList() ?? [],
    };

    internal override IReadOnlyList<GraphChunkHit> Map(IReadOnlyList<IGraphRow> rows, bool truncated) => RetrievalRows.Map(rows);
}

internal static class RetrievalRows
{
    public static IReadOnlyList<GraphChunkHit> Map(IReadOnlyList<IGraphRow> rows) =>
        [.. rows.Select(r => new GraphChunkHit(Chunk(r["chunk"] as IReadOnlyDictionary<string, object> ?? new Dictionary<string, object>()), Number(r["score"])))];

    private static ChunkRecord Chunk(IReadOnlyDictionary<string, object> c) => new()
    {
        TenantId = Str(c, ChunkSchema.TenantId) ?? "",
        DocId = Str(c, ChunkSchema.DocId) ?? "",
        ChunkId = Str(c, ChunkSchema.ChunkId) ?? "",
        SourceType = Str(c, ChunkSchema.SourceType) ?? "",
        SourcePath = Str(c, ChunkSchema.SourcePath) ?? "",
        SectionPath = Str(c, ChunkSchema.SectionPath) ?? "",
        Symbol = Str(c, ChunkSchema.Symbol),
        UpdatedAt = DateTimeOffset.TryParse(Str(c, ChunkSchema.UpdatedAt), out var u) ? u : DateTimeOffset.MinValue,
        ModelVersion = Str(c, ChunkSchema.ModelVersion) ?? "",
        Text = Str(c, ChunkSchema.Text) ?? "",
        Context = Str(c, ChunkSchema.Context),
        ContentHash = Str(c, ChunkSchema.ContentHash) ?? "",
        StartLine = c.TryGetValue(ChunkSchema.StartLine, out var s) && s is long sl ? (int)sl : null,
        EndLine = c.TryGetValue(ChunkSchema.EndLine, out var e) && e is long el ? (int)el : null,
    };

    private static string? Str(IReadOnlyDictionary<string, object> c, string key) => c.TryGetValue(key, out var v) ? v as string : null;

    private static double Number(object? value) => value switch
    {
        double d => d,
        float f => f,
        long l => l,
        _ => 0,
    };
}
