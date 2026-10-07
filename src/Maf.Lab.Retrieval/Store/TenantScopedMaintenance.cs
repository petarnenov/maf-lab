using Maf.Lab.Domain.Admin;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Retrieval.Configuration;
using Maf.Lab.Retrieval.Sparse;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using static Qdrant.Client.Grpc.Conditions;

namespace Maf.Lab.Retrieval.Store;

/// <param name="Dense">Every dense vector of the chunk, by vector name — all of them, so no write drops one.</param>
public sealed record ChunkWrite(ChunkRecord Chunk, IReadOnlyDictionary<string, float[]> Dense, SparseVectorData Sparse);

public sealed record MigrationCandidate(Guid PointId, string Text, string? Context, string SectionPath);

/// <summary>
/// Writes and administrative reads of chunk points. Every operation is scoped to exactly one tenant,
/// which comes from the corpus layout (indexer) or from a TENANT_ADMIN principal — never from request input.
/// </summary>
/// <summary>The chunk store of a domain whose chunks are in a collection of their own, on the core's Qdrant connection.</summary>
public static class DomainChunkStore
{
    /// <summary>
    /// A store over the given collection with the core's Qdrant settings otherwise (a plugin's collection): a plugin
    /// names its collection here and never reaches the Qdrant client itself (spec plugins).
    /// </summary>
    public static TenantScopedMaintenance For(IServiceProvider services, string collection, string metaCollection)
    {
        var qdrant = services.GetRequiredService<IOptions<QdrantOptions>>().Value;
        return new TenantScopedMaintenance(services.GetRequiredService<QdrantClient>(), Options.Create(new QdrantOptions
        {
            Host = qdrant.Host, GrpcPort = qdrant.GrpcPort, Https = qdrant.Https, ApiKey = qdrant.ApiKey, PayloadM = qdrant.PayloadM,
            Collection = collection,
            MetaCollection = metaCollection,
        }));
    }
}

public sealed class TenantScopedMaintenance(QdrantClient client, IOptions<QdrantOptions> options)
{
    private readonly string _collection = options.Value.Collection;

    /// <summary>
    /// Replaces a document's chunks: deletes every point of that doc_id, then upserts the new version's points, so no
    /// point of an old version can survive the replacement. For the moment between the two steps the document is
    /// absent from search; if the process stops there, the next indexing run finds it missing and writes it again.
    /// </summary>
    public async Task<(int Written, long Deleted)> ReplaceDocumentAsync(TenantId tenant, string docId, IReadOnlyList<ChunkWrite> writes, CancellationToken ct)
    {
        if (writes.Any(w => w.Chunk.TenantId != tenant.Value || w.Chunk.DocId != docId))
        {
            throw new InvalidOperationException("Chunk tenant or doc_id does not match the document being replaced.");
        }

        var deleted = await DeleteDocumentAsync(tenant, docId, ct);
        foreach (var batch in writes.Select(ToPoint).Chunk(64))
        {
            await client.UpsertAsync(_collection, batch, wait: true, cancellationToken: ct);
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
                : new IndexedDocument(chunk.DocId, chunk.SourcePath, chunk.UpdatedAt, chunk.ContentHash, chunk.ModelVersion, 1,
                    chunk.DenseModelVersions);
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

    /// <summary>Next batch of the tenant's points whose <paramref name="denseVector"/> is not yet from <paramref name="targetModelVersion"/>.</summary>
    public async Task<IReadOnlyList<MigrationCandidate>> NextMigrationBatchAsync(TenantId tenant, string denseVector, string targetModelVersion, uint batchSize, CancellationToken ct)
    {
        var filter = TenantFilter.For(tenant);
        filter.MustNot.Add(MatchKeyword(ChunkSchema.ModelVersionOf(denseVector), targetModelVersion));
        var page = await client.ScrollAsync(_collection, filter, batchSize, payloadSelector: true, vectorsSelector: false, cancellationToken: ct);
        return page.Result.Select(p =>
        {
            var c = PayloadMapper.FromPayload(p.Payload);
            return new MigrationCandidate(Guid.Parse(p.Id.Uuid), c.Text, c.Context, c.SectionPath);
        }).ToList();
    }

    /// <summary>
    /// Conditional update: the new vector is written only to points of this tenant whose vector is still from an old
    /// model, so re-running after a crash never double-applies. That vector's model version is set afterwards — and
    /// only that one, so the other vectors' records stay true; <c>model_version</c> moves only when
    /// <paramref name="isIndexingVector"/>. A crash between the two steps just re-embeds those points next run.
    /// </summary>
    public async Task ApplyMigrationBatchAsync(TenantId tenant, string denseVector, string targetModelVersion, bool isIndexingVector,
        IReadOnlyList<(Guid PointId, float[] Vector)> vectors, CancellationToken ct)
    {
        if (vectors.Count == 0)
        {
            return;
        }
        var condition = TenantFilter.For(tenant);
        condition.MustNot.Add(MatchKeyword(ChunkSchema.ModelVersionOf(denseVector), targetModelVersion));

        var pointVectors = vectors.Select(v => new PointVectors
        {
            Id = new PointId { Uuid = v.PointId.ToString() },
            Vectors = new Vectors { Vectors_ = new NamedVectors { Vectors = { [denseVector] = new Vector { Dense = new DenseVector { Data = { v.Vector } } } } } },
        }).ToList();
        await client.UpdateVectorsAsync(_collection, pointVectors, condition, wait: true, cancellationToken: ct);

        var scoped = TenantFilter.For(tenant);
        scoped.Must.Add(HasId(vectors.Select(v => v.PointId).ToList()));
        var versions = new Dictionary<string, Value> { [ChunkSchema.ModelVersionOf(denseVector)] = targetModelVersion };
        if (isIndexingVector)
        {
            versions[ChunkSchema.ModelVersion] = targetModelVersion;
        }
        await client.SetPayloadAsync(_collection, versions, scoped, wait: true, cancellationToken: ct);
    }

    /// <summary>
    /// Every point of the collection with its payload, its <paramref name="denseVector"/> and its BM25 sparse vector: what
    /// the Neo4j retrieval spike copies (neo4j-retrieval-spike). A maintenance read for the indexer only; no tool reaches it.
    /// </summary>
    public async IAsyncEnumerable<ChunkVectors> ListChunkVectorsAsync(string denseVector,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var point in ScrollAsync(new Filter(), withVectors: true, ct))
        {
            var named = point.Vectors?.Vectors?.Vectors;
            float[]? dense = null;
            SparseVectorData? sparse = null;
            if (named is not null && named.TryGetValue(denseVector, out var d))
            {
                dense = d.Dense is { } dv ? [.. dv.Data] : null;
            }
            if (named is not null && named.TryGetValue(ChunkSchema.SparseVector, out var sv))
            {
                sparse = sv.Sparse is { } sp ? new SparseVectorData([.. sp.Indices], [.. sp.Values]) : null;
            }
            yield return new ChunkVectors(PayloadMapper.FromPayload(point.Payload), dense, sparse);
        }
    }

    /// <summary>How many points the collection holds: what the spike's copy is checked against.</summary>
    public async Task<long> CountAsync(CancellationToken ct) => (long)await client.CountAsync(_collection, exact: true, cancellationToken: ct);

    public string Collection => _collection;

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
        foreach (var (name, vector) in w.Dense)
        {
            named.Vectors[name] = new Vector { Dense = new DenseVector { Data = { vector } } };
        }
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

/// <summary>A chunk with the vectors it was indexed with; either vector may be missing on an old point.</summary>
public sealed record ChunkVectors(ChunkRecord Chunk, float[]? Dense, SparseVectorData? Sparse);
