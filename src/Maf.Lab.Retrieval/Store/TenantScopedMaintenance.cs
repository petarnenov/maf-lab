using Maf.Lab.Domain.Admin;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Retrieval.Configuration;
using Maf.Lab.Retrieval.Sparse;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using static Qdrant.Client.Grpc.Conditions;

namespace Maf.Lab.Retrieval.Store;

public sealed record ChunkWrite(ChunkRecord Chunk, string DenseVector, float[] Dense, SparseVectorData Sparse);

public sealed record MigrationCandidate(Guid PointId, string Text, string? Context, string SectionPath);

/// <summary>
/// Writes and administrative reads of chunk points. Every operation is scoped to exactly one tenant,
/// which comes from the corpus layout (indexer) or from a FIRM_ADMIN principal — never from request input.
/// </summary>
public sealed class TenantScopedMaintenance(QdrantClient client, IOptions<QdrantOptions> options)
{
    private readonly string _collection = options.Value.Collection;

    /// <summary>
    /// Replaces a document's chunks: upserts the new points, then deletes every other point of that doc_id.
    /// Upsert-then-delete leaves no window in which the document is missing from search.
    /// </summary>
    public async Task<(int Written, long Deleted)> ReplaceDocumentAsync(TenantId tenant, string docId, IReadOnlyList<ChunkWrite> writes, CancellationToken ct)
    {
        if (writes.Any(w => w.Chunk.TenantId != tenant.Value || w.Chunk.DocId != docId))
        {
            throw new InvalidOperationException("Chunk tenant or doc_id does not match the document being replaced.");
        }

        if (writes.Count > 0)
        {
            var points = writes.Select(ToPoint).ToList();
            foreach (var batch in points.Chunk(64))
            {
                await client.UpsertAsync(_collection, batch, wait: true, cancellationToken: ct);
            }
        }

        var stale = DocFilter(tenant, docId);
        if (writes.Count > 0)
        {
            stale.MustNot.Add(HasId(writes.Select(w => w.Chunk.PointId).ToList()));
        }
        var deleted = (long)await client.CountAsync(_collection, stale, exact: true, cancellationToken: ct);
        if (deleted > 0)
        {
            await client.DeleteAsync(_collection, stale, wait: true, cancellationToken: ct);
        }
        return (writes.Count, deleted);
    }

    public async Task<long> DeleteDocumentAsync(TenantId tenant, string docId, CancellationToken ct)
    {
        var filter = DocFilter(tenant, docId);
        var count = (long)await client.CountAsync(_collection, filter, exact: true, cancellationToken: ct);
        if (count > 0)
        {
            await client.DeleteAsync(_collection, filter, wait: true, cancellationToken: ct);
        }
        return count;
    }

    /// <summary>One entry per indexed document of the tenant (aggregated from its chunks).</summary>
    public async Task<IReadOnlyList<IndexedDocument>> ListDocumentsAsync(TenantId tenant, CancellationToken ct)
    {
        var docs = new Dictionary<string, IndexedDocument>(StringComparer.Ordinal);
        await foreach (var point in ScrollAsync(TenantFilter.For(tenant), withVectors: false, ct))
        {
            var chunk = PayloadMapper.FromPayload(point.Payload);
            docs[chunk.DocId] = docs.TryGetValue(chunk.DocId, out var d)
                ? d with { Chunks = d.Chunks + 1, UpdatedAt = chunk.UpdatedAt > d.UpdatedAt ? chunk.UpdatedAt : d.UpdatedAt }
                : new IndexedDocument(chunk.DocId, chunk.SourcePath, chunk.UpdatedAt, chunk.ContentHash, chunk.ModelVersion, 1);
        }
        return docs.Values.OrderBy(d => d.DocId, StringComparer.Ordinal).ToList();
    }

    /// <summary>All chunks of a tenant; used to rebuild corpus statistics and by evals.</summary>
    public async IAsyncEnumerable<ChunkRecord> ListChunksAsync(TenantId tenant, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var point in ScrollAsync(TenantFilter.For(tenant), withVectors: false, ct))
        {
            yield return PayloadMapper.FromPayload(point.Payload);
        }
    }

    /// <summary>Chunk ids of one section of one document (review-time lookup for labeling).</summary>
    public async Task<IReadOnlyList<string>> ChunkIdsForSectionAsync(TenantId tenant, string docId, string sectionPath, CancellationToken ct)
    {
        var filter = DocFilter(tenant, docId);
        filter.Must.Add(MatchKeyword(ChunkSchema.SectionPath, sectionPath));
        var ids = new List<string>();
        await foreach (var point in ScrollAsync(filter, withVectors: false, ct))
        {
            ids.Add(PayloadMapper.FromPayload(point.Payload).ChunkId);
        }
        return ids;
    }

    public async Task<IReadOnlyList<ModelVersionCount>> ModelVersionsAsync(TenantId tenant, CancellationToken ct)
    {
        var facets = await client.FacetAsync(_collection, ChunkSchema.ModelVersion, TenantFilter.For(tenant), limit: 20, exact: true, cancellationToken: ct);
        return facets.Hits.Select(f => new ModelVersionCount(f.Value.StringValue, (long)f.Count)).ToList();
    }

    public async Task<long> CountAsync(TenantId tenant, CancellationToken ct) =>
        (long)await client.CountAsync(_collection, TenantFilter.For(tenant), exact: true, cancellationToken: ct);

    /// <summary>Next batch of the tenant's points whose model_version is not yet <paramref name="targetModelVersion"/>.</summary>
    public async Task<IReadOnlyList<MigrationCandidate>> NextMigrationBatchAsync(TenantId tenant, string targetModelVersion, uint batchSize, CancellationToken ct)
    {
        var filter = TenantFilter.For(tenant);
        filter.MustNot.Add(MatchKeyword(ChunkSchema.ModelVersion, targetModelVersion));
        var page = await client.ScrollAsync(_collection, filter, batchSize, payloadSelector: true, vectorsSelector: false, cancellationToken: ct);
        return page.Result.Select(p =>
        {
            var c = PayloadMapper.FromPayload(p.Payload);
            return new MigrationCandidate(Guid.Parse(p.Id.Uuid), c.Text, c.Context, c.SectionPath);
        }).ToList();
    }

    /// <summary>
    /// Conditional update: the new vector is written only to points of this tenant that are still on an old
    /// model version, so re-running after a crash never double-applies. model_version is set afterwards;
    /// a crash between the two steps just re-embeds those points on the next run.
    /// </summary>
    public async Task ApplyMigrationBatchAsync(TenantId tenant, string denseVector, string targetModelVersion, IReadOnlyList<(Guid PointId, float[] Vector)> vectors, CancellationToken ct)
    {
        if (vectors.Count == 0)
        {
            return;
        }
        var condition = TenantFilter.For(tenant);
        condition.MustNot.Add(MatchKeyword(ChunkSchema.ModelVersion, targetModelVersion));

        var pointVectors = vectors.Select(v => new PointVectors
        {
            Id = new PointId { Uuid = v.PointId.ToString() },
            Vectors = new Vectors { Vectors_ = new NamedVectors { Vectors = { [denseVector] = new Vector { Dense = new DenseVector { Data = { v.Vector } } } } } },
        }).ToList();
        await client.UpdateVectorsAsync(_collection, pointVectors, condition, wait: true, cancellationToken: ct);

        var scoped = TenantFilter.For(tenant);
        scoped.Must.Add(HasId(vectors.Select(v => v.PointId).ToList()));
        await client.SetPayloadAsync(_collection, new Dictionary<string, Value> { [ChunkSchema.ModelVersion] = targetModelVersion }, scoped, wait: true, cancellationToken: ct);
    }

    private async IAsyncEnumerable<RetrievedPoint> ScrollAsync(Filter filter, bool withVectors, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        PointId? offset = null;
        do
        {
            var page = await client.ScrollAsync(_collection, filter, 256, offset, payloadSelector: true, vectorsSelector: withVectors, cancellationToken: ct);
            foreach (var p in page.Result)
            {
                yield return p;
            }
            offset = page.NextPageOffset;
        }
        while (offset is not null);
    }

    private static Filter DocFilter(TenantId tenant, string docId)
    {
        var filter = TenantFilter.For(tenant);
        filter.Must.Add(MatchKeyword(ChunkSchema.DocId, docId));
        return filter;
    }

    private static PointStruct ToPoint(ChunkWrite w)
    {
        var point = new PointStruct { Id = new PointId { Uuid = w.Chunk.PointId.ToString() } };
        var named = new NamedVectors();
        named.Vectors[w.DenseVector] = new Vector { Dense = new DenseVector { Data = { w.Dense } } };
        if (!w.Sparse.IsEmpty)
        {
            named.Vectors[ChunkSchema.SparseVector] = new Vector { Sparse = new Qdrant.Client.Grpc.SparseVector { Indices = { w.Sparse.Indices }, Values = { w.Sparse.Values } } };
        }
        point.Vectors = new Vectors { Vectors_ = named };
        foreach (var (k, v) in PayloadMapper.ToPayload(w.Chunk))
        {
            point.Payload[k] = v;
        }
        return point;
    }
}
