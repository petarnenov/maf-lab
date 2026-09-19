using System.Text.Json.Nodes;
using Maf.Lab.Retrieval.Store;

namespace Maf.Lab.Retrieval.Search;

/// <summary>
/// Collects what hybrid search did for one query (settings, BM25 terms, per-branch candidates, rerank, timings) for the
/// behind-the-scenes monitor. Only ever holds candidates from the caller's tenant scope.
/// </summary>
public sealed class SearchDiagnostics
{
    public JsonArray TenantScope { get; } = [];
    public JsonObject Settings { get; } = [];
    public JsonObject Query { get; } = [];
    public JsonArray Dense { get; set; } = [];
    public JsonArray Sparse { get; set; } = [];
    public JsonArray Fused { get; set; } = [];
    public JsonArray? Rerank { get; set; }
    public JsonObject Timings { get; } = [];

    public static JsonArray Candidates(IEnumerable<ScoredChunk> chunks) =>
        new(chunks.Select((c, i) => (JsonNode)new JsonObject
        {
            ["rank"] = i + 1,
            ["chunkId"] = c.Chunk.ChunkId,
            ["docId"] = c.Chunk.DocId,
            ["tenantId"] = c.Chunk.TenantId,
            ["sectionPath"] = c.Chunk.SectionPath,
            ["score"] = Math.Round(c.Score, 5),
        }).ToArray());

    public JsonObject ToJson(string instance) => new()
    {
        ["instance"] = instance,
        ["tenantScope"] = TenantScope.DeepClone(),
        ["settings"] = Settings.DeepClone(),
        ["query"] = Query.DeepClone(),
        ["dense"] = Dense.DeepClone(),
        ["sparse"] = Sparse.DeepClone(),
        ["fused"] = Fused.DeepClone(),
        ["rerank"] = Rerank?.DeepClone(),
        ["timings"] = Timings.DeepClone(),
    };
}
