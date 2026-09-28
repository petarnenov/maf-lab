using System.Text.Json.Nodes;
using Maf.Lab.Retrieval.Rerank;
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

    /// <summary>Jev's relevance judgment, when the gate or the Jev reranker asked for one.</summary>
    public JsonObject? Relevance { get; set; }
    public JsonObject Timings { get; } = [];

    /// <summary>
    /// False skips the per-branch re-queries: for a caller that wants what the search did (the retrieval eval, counting
    /// judge failures), not the monitor's picture of each branch.
    /// </summary>
    public bool Branches { get; init; } = true;

    /// <summary>
    /// A branch's candidates for the monitor. <paramref name="floor"/> marks which of them the relevance floor
    /// would drop — the branch lists are gathered unfiltered on purpose, because "found candidates, none close
    /// enough" and "found nothing at all" have different causes and a filtered list cannot tell them apart.
    /// </summary>
    public static JsonArray Candidates(IEnumerable<ScoredChunk> chunks, float? floor = null) =>
        new(chunks.Select((c, i) => (JsonNode)new JsonObject
        {
            ["rank"] = i + 1,
            ["chunkId"] = c.Chunk.ChunkId,
            ["docId"] = c.Chunk.DocId,
            ["tenantId"] = c.Chunk.TenantId,
            ["sectionPath"] = c.Chunk.SectionPath,
            ["score"] = Math.Round(c.Score, 5),
            ["belowFloor"] = floor is { } f && c.Score < f,
        }).ToArray());

    /// <summary>
    /// What the relevance judge said about the fused candidates it saw, and what the gate did with it. A search the gate
    /// silenced still shows every candidate it withheld, with its probability, so "found nothing" and "found something,
    /// judged none of it relevant" stay apart.
    /// </summary>
    public static JsonObject RelevanceOf(RelevanceJudgement judgement, IReadOnlyList<ScoredChunk> candidates, bool gate, double floor, bool silenced) => new()
    {
        ["gate"] = gate,
        ["floor"] = floor,
        ["judged"] = judgement.Scores?.Count ?? 0,
        ["max"] = judgement.Max,
        ["silenced"] = silenced,
        ["model"] = judgement.Model,
        ["durationMs"] = Math.Round(judgement.DurationMs),
        ["reason"] = judgement.Reason,
        ["scores"] = judgement.Scores is { } scores
            ? new JsonArray(scores.Select((p, i) => (JsonNode)new JsonObject { ["chunkId"] = candidates[i].Chunk.ChunkId, ["p"] = p }).ToArray())
            : null,
    };

    /// <summary>
    /// The judge's verdict on one search as numbers and flags only — no query, passage, chunk or document id, and no
    /// probability per candidate. Returned with every judged search whether or not diagnostics were asked for.
    /// </summary>
    /// <param name="reranker">The reranker in use (<see cref="RerankerKinds"/>), or null when rerank is off.</param>
    /// <param name="rerankedByJev">Whether Jev's answer actually ordered the results — false when silenced or unanswered.</param>
    public static JsonObject SummaryOf(RelevanceJudgement judgement, bool gate, double floor, bool silenced, string? reranker, bool rerankedByJev) => new()
    {
        ["gate"] = gate,
        ["reranker"] = reranker,
        ["floor"] = floor,
        ["judged"] = judgement.Scores?.Count ?? 0,
        ["max"] = judgement.Max,
        ["silenced"] = silenced,
        ["rerankedByJev"] = rerankedByJev,
        ["model"] = judgement.Model,
        ["durationMs"] = Math.Round(judgement.DurationMs),
        ["reason"] = judgement.Reason,
    };

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
        ["relevance"] = Relevance?.DeepClone(),
        ["timings"] = Timings.DeepClone(),
    };
}
