using System.Diagnostics;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Indexing.Pipeline;
using Maf.Lab.Retrieval.Configuration;
using Maf.Lab.Retrieval.Graph;
using Maf.Lab.Retrieval.Store;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Indexing.Graph;

/// <summary>What one copy of a collection into Neo4j did.</summary>
public sealed record RetrievalCopySummary(string Collection, string Source, int Chunks, int Terms, int Edges, int Written, int Unchanged,
    int RemovedNodes, int RemovedEdges, int Rejected, double Seconds);

/// <summary>
/// Copies one Qdrant collection's chunks into Neo4j for the retrieval spike (neo4j-retrieval-spike): each point becomes a
/// <c>RetrievalChunk</c> with its payload and its dense vector, and each BM25 term of its sparse vector a shared
/// <c>Term</c> linked by <c>OCCURS_IN {w}</c> — the same ids and weights, so both stores search identical data. Nothing
/// is embedded again. Reads through the maintenance scroll, writes through the one graph write path, idempotent.
/// </summary>
public sealed class RetrievalCopyService(TenantScopedMaintenance qdrant, TenantScopedGraphMaintenance graph, IOptions<RetrievalOptions> retrieval)
{
    private const int WriteBatch = 500;

    public async Task<RetrievalCopySummary> RunAsync(IProgress<IndexProgress>? progress, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        var collection = qdrant.Collection;
        var source = RetrievalGraph.SourceOf(collection);
        var denseVector = retrieval.Value.DenseVector;
        await graph.EnsureSchemaAsync(ct);
        var runId = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..23];

        var total = (int)await qdrant.CountAsync(ct);
        progress?.Report(new IndexProgress($"{collection}: reading", 0, total));
        var points = new List<ChunkVectors>(total);
        await foreach (var point in qdrant.ListChunkVectorsAsync(denseVector, ct))
        {
            points.Add(point);
            if (points.Count % 256 == 0)
            {
                progress?.Report(new IndexProgress($"{collection}: reading", points.Count, total));
            }
        }

        var (chunks, terms, edges, rejected) = Build(collection, points);
        var work = points.Count + chunks.Count + terms.Count + edges.Count;
        var done = points.Count;
        progress?.Report(new IndexProgress($"{collection}: writing", done, work));
        var counts = GraphWriteCounts.None;
        foreach (var batch in chunks.Concat(terms).Chunk(WriteBatch))
        {
            counts = counts.Add(await graph.WriteNodesAsync(source, runId, batch, ct));
            done += batch.Length;
            progress?.Report(new IndexProgress($"{collection}: writing", done, work));
        }
        foreach (var batch in edges.Chunk(WriteBatch))
        {
            counts = counts.Add(await graph.WriteEdgesAsync(source, runId, batch, ct));
            done += batch.Length;
            progress?.Report(new IndexProgress($"{collection}: writing", done, work));
        }
        progress?.Report(new IndexProgress($"{collection}: removing stale", done, work));
        var removed = await graph.RemoveStaleAsync(source, runId, ct);
        return new RetrievalCopySummary(collection, source, chunks.Count, terms.Count, edges.Count, counts.Written, counts.Unchanged,
            removed.Nodes, removed.Edges, rejected + counts.Rejected, Math.Round(watch.Elapsed.TotalSeconds, 1));
    }

    /// <summary>
    /// The nodes and edges of a collection's copy. A point without a tenant, without a dense vector or with one of another
    /// length is rejected rather than copied half: the comparison must run over the same chunks on both sides.
    /// </summary>
    public static (List<GraphNode> Chunks, List<GraphNode> Terms, List<GraphEdge> Edges, int Rejected) Build(string collection,
        IReadOnlyList<ChunkVectors> points)
    {
        var chunks = new List<GraphNode>(points.Count);
        var termIds = new SortedSet<uint>();
        var edges = new List<GraphEdge>();
        var rejected = 0;
        foreach (var point in points)
        {
            var c = point.Chunk;
            if (!TenantId.TryParse(c.TenantId, out var tenant) || point.Dense is not { Length: RetrievalGraph.DenseDimensions } dense)
            {
                rejected++;
                continue;
            }
            var key = RetrievalGraph.ChunkKey(collection, c.ChunkId);
            chunks.Add(new GraphNode(GraphLabels.RetrievalChunk, tenant, key, new Dictionary<string, object?>
            {
                [RetrievalGraph.CollectionProperty] = collection,
                [ChunkSchema.DocId] = c.DocId,
                [ChunkSchema.ChunkId] = c.ChunkId,
                [ChunkSchema.SourceType] = c.SourceType,
                [ChunkSchema.SourcePath] = c.SourcePath,
                [ChunkSchema.SectionPath] = c.SectionPath,
                [ChunkSchema.Symbol] = c.Symbol,
                [ChunkSchema.UpdatedAt] = c.UpdatedAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                [ChunkSchema.ModelVersion] = c.ModelVersion,
                [ChunkSchema.Text] = c.Text,
                [ChunkSchema.Context] = c.Context,
                [ChunkSchema.ContentHash] = c.ContentHash,
                [ChunkSchema.StartLine] = c.StartLine,
                [ChunkSchema.EndLine] = c.EndLine,
                // The driver has no single-precision list; widening is exact, and the index stores what Qdrant scored.
                [RetrievalGraph.DenseProperty] = dense.Select(x => (double)x).ToList(),
            }));
            if (point.Sparse is { } sparse)
            {
                for (var i = 0; i < sparse.Indices.Length; i++)
                {
                    termIds.Add(sparse.Indices[i]);
                    edges.Add(new GraphEdge(GraphLabels.Term, TenantId.Shared, RetrievalGraph.TermKey(collection, sparse.Indices[i]),
                        GraphRelations.OccursIn, GraphLabels.RetrievalChunk, tenant, key,
                        new Dictionary<string, object?> { [RetrievalGraph.WeightProperty] = (double)sparse.Values[i] }));
                }
            }
        }
        // A term id says nothing about a firm, so terms are shared; the tenant is on the chunk a term leads to.
        var terms = termIds.Select(id => new GraphNode(GraphLabels.Term, TenantId.Shared, RetrievalGraph.TermKey(collection, id),
            new Dictionary<string, object?> { [RetrievalGraph.CollectionProperty] = collection, ["term_id"] = (long)id })).ToList();
        return (chunks, terms, edges, rejected);
    }
}
