using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Retrieval.Configuration;
using Maf.Lab.Retrieval.Store;

namespace Maf.Lab.Retrieval.Graph;

/// <summary>
/// The retrieval spike's chunk search over Neo4j (neo4j-retrieval-spike), with the contract of
/// <see cref="TenantScopedSearch"/>: a dense and a sparse branch, each restricted to the principal's readable tenants
/// inside its search, each with its own floor and at least five times the requested count, fused here as Qdrant fuses
/// them. Built only by the eval harness; no service registers it. It reads only through <see cref="IGraphReader"/>.
/// </summary>
public sealed class GraphChunkSearch(IGraphReader graph, string collection) : IChunkSearch
{
    public string Collection => collection;

    public async Task<IReadOnlyList<ScoredChunk>> QueryAsync(Principal principal, SearchRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(principal);
        var hybrid = request.Mode == RetrievalModes.Hybrid;
        var branchLimit = hybrid ? Math.Max(request.PrefetchLimit, request.Limit * 5) : request.Limit;
        var useDense = request.Dense is { Length: > 0 } && request.Mode is RetrievalModes.Hybrid or RetrievalModes.Dense;
        var useSparse = request.Sparse is { IsEmpty: false } && request.Mode is RetrievalModes.Hybrid or RetrievalModes.Sparse;

        IReadOnlyList<ScoredChunk> dense = [];
        if (useDense)
        {
            var hits = await graph.ReadAsync(principal, new ChunkDenseSearch(collection, request.Dense!, request.SourceTypes, branchLimit), ct);
            dense = Floor(hits.Select(h => new ScoredChunk(h.Chunk, CosineOf(h.Score))), request.DenseFloor);
        }
        IReadOnlyList<ScoredChunk> sparse = [];
        if (useSparse)
        {
            var hits = await graph.ReadAsync(principal, new ChunkSparseSearch(collection, request.Sparse!, request.SourceTypes, branchLimit), ct);
            sparse = Floor(hits.Select(h => new ScoredChunk(h.Chunk, h.Score)), request.SparseFloor);
        }

        return request.Mode switch
        {
            RetrievalModes.Dense => [.. dense.Take(request.Limit)],
            RetrievalModes.Sparse => [.. sparse.Take(request.Limit)],
            RetrievalModes.Hybrid when dense.Count + sparse.Count == 0 => [],
            RetrievalModes.Hybrid => [.. ChunkFusion.Fuse(request.Fusion, [dense, sparse]).Take(request.Limit)],
            _ => [],
        };
    }

    /// <summary>
    /// Neo4j's cosine score is normalised as (1 + cos) / 2; Qdrant's is the cosine itself. Converting back lets a dense
    /// floor calibrated on Qdrant apply unchanged.
    /// </summary>
    public static double CosineOf(double neo4jScore) => 2 * neo4jScore - 1;

    /// <summary>A branch's candidates at or above its floor; null leaves the branch unfiltered, as on Qdrant.</summary>
    internal static IReadOnlyList<ScoredChunk> Floor(IEnumerable<ScoredChunk> candidates, float? floor) =>
        [.. floor is { } f ? candidates.Where(c => c.Score >= f) : candidates];
}

/// <summary>
/// Qdrant's two fusions, done in code for the spike. RRF: Σ 1 / (k + rank) with k = 2 and zero-based ranks. DBSF: each
/// branch's scores remapped by (s − (μ − 3σ)) / 6σ with the sample standard deviation, unclipped, then summed; a branch
/// whose scores are all equal, or that has one, gives 0.5. Ties keep the chunk id order, so a result is deterministic.
/// </summary>
public static class ChunkFusion
{
    public const int RrfK = 2;

    public static IReadOnlyList<ScoredChunk> Fuse(string fusion, IReadOnlyList<IReadOnlyList<ScoredChunk>> branches) =>
        fusion == FusionModes.Dbsf ? Dbsf(branches) : Rrf(branches);

    public static IReadOnlyList<ScoredChunk> Rrf(IReadOnlyList<IReadOnlyList<ScoredChunk>> branches)
    {
        var scores = new Dictionary<string, (ChunkRecord Chunk, double Score)>(StringComparer.Ordinal);
        foreach (var branch in branches)
        {
            for (var rank = 0; rank < branch.Count; rank++)
            {
                var c = branch[rank].Chunk;
                var add = 1.0 / (RrfK + rank);
                scores[c.ChunkId] = scores.TryGetValue(c.ChunkId, out var seen) ? (seen.Chunk, seen.Score + add) : (c, add);
            }
        }
        return Ordered(scores);
    }

    public static IReadOnlyList<ScoredChunk> Dbsf(IReadOnlyList<IReadOnlyList<ScoredChunk>> branches)
    {
        var scores = new Dictionary<string, (ChunkRecord Chunk, double Score)>(StringComparer.Ordinal);
        foreach (var branch in branches.Where(b => b.Count > 0))
        {
            var values = branch.Select(b => b.Score).ToList();
            var mean = values.Average();
            var sd = values.Count > 1 ? Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1)) : 0;
            foreach (var b in branch)
            {
                var normalised = sd == 0 ? 0.5 : (b.Score - (mean - 3 * sd)) / (6 * sd);
                var id = b.Chunk.ChunkId;
                scores[id] = scores.TryGetValue(id, out var seen) ? (seen.Chunk, seen.Score + normalised) : (b.Chunk, normalised);
            }
        }
        return Ordered(scores);
    }

    private static IReadOnlyList<ScoredChunk> Ordered(Dictionary<string, (ChunkRecord Chunk, double Score)> scores) =>
        [.. scores.Values.OrderByDescending(s => s.Score).ThenBy(s => s.Chunk.ChunkId, StringComparer.Ordinal).Select(s => new ScoredChunk(s.Chunk, s.Score))];
}
