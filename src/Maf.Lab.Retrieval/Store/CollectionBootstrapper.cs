using Maf.Lab.Retrieval.Configuration;
using Maf.Lab.Retrieval.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace Maf.Lab.Retrieval.Store;

/// <summary>
/// Schema-only operations: creates the chunk collection with payload-based multitenancy
/// (tenant key index, HNSW m=0 + payload_m) and adds dense vectors for model migration.
/// Reads and writes of points live in TenantScopedSearch / TenantScopedMaintenance.
/// </summary>
public sealed class CollectionBootstrapper(
    QdrantClient client,
    IOptions<QdrantOptions> qdrant,
    ModelProviders models,
    ILogger<CollectionBootstrapper> logger)
{
    private readonly QdrantOptions _qdrant = qdrant.Value;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _ready;

    public async Task EnsureAsync(CancellationToken ct = default)
    {
        if (_ready)
        {
            return;
        }
        await _gate.WaitAsync(ct);
        try
        {
            if (_ready)
            {
                return;
            }
            await EnsureChunkCollectionAsync(ct);
            await EnsureMetaCollectionAsync(ct);
            _ready = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureChunkCollectionAsync(CancellationToken ct)
    {
        if (!await client.CollectionExistsAsync(_qdrant.Collection, ct))
        {
            var vectors = new VectorParamsMap();
            foreach (var (name, profile) in models.Profiles)
            {
                vectors.Map[name] = new VectorParams { Size = (ulong)profile.Dimensions, Distance = Distance.Cosine };
            }
            var sparse = new SparseVectorConfig();
            sparse.Map[ChunkSchema.SparseVector] = new SparseVectorParams();

            await client.CreateCollectionAsync(
                _qdrant.Collection,
                vectors,
                hnswConfig: new HnswConfigDiff { M = 0, PayloadM = _qdrant.PayloadM },
                sparseVectorsConfig: sparse,
                cancellationToken: ct);
            logger.LogInformation("Created collection {Collection} with {VectorCount} dense vectors", _qdrant.Collection, vectors.Map.Count);
        }
        else
        {
            await AddMissingDenseVectorsAsync(ct);
        }

        // Only the payload indexes the collection lacks are created: each creation is a write (a WAL entry), and a
        // repeat index run over an unchanged corpus should write nothing.
        var indexed = (await client.GetCollectionInfoAsync(_qdrant.Collection, ct)).PayloadSchema.Keys.ToHashSet(StringComparer.Ordinal);
        if (!indexed.Contains(ChunkSchema.TenantId))
        {
            await client.CreatePayloadIndexAsync(_qdrant.Collection, ChunkSchema.TenantId, PayloadSchemaType.Keyword,
                new PayloadIndexParams { KeywordIndexParams = new KeywordIndexParams { IsTenant = true } }, cancellationToken: ct);
        }
        foreach (var field in new[] { ChunkSchema.SourceType, ChunkSchema.DocId, ChunkSchema.ModelVersion }
                     .Concat(models.Profiles.Keys.Select(ChunkSchema.ModelVersionOf))
                     .Where(f => !indexed.Contains(f)))
        {
            await client.CreatePayloadIndexAsync(_qdrant.Collection, field, PayloadSchemaType.Keyword, cancellationToken: ct);
        }
        if (!indexed.Contains(ChunkSchema.UpdatedAt))
        {
            await client.CreatePayloadIndexAsync(_qdrant.Collection, ChunkSchema.UpdatedAt, PayloadSchemaType.Datetime, cancellationToken: ct);
        }
    }

    /// <summary>
    /// Deletes the chunk collection so the next <see cref="EnsureAsync"/> creates it with every configured dense vector —
    /// the only way to provision a new named vector. Called by the indexer's explicit rebuild, never implicitly.
    /// Returns how many points were discarded; the corpus is rebuilt from source by indexing afterwards.
    /// </summary>
    public async Task<ulong> DeleteChunkCollectionAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            _ready = false;
            if (!await client.CollectionExistsAsync(_qdrant.Collection, ct))
            {
                return 0;
            }
            // The collection's own point count (schema metadata), not a Count query: no data-plane read of chunk points
            // happens outside the tenant-scoped classes, even to report a number.
            var points = (await client.GetCollectionInfoAsync(_qdrant.Collection, ct)).PointsCount;
            await client.DeleteCollectionAsync(_qdrant.Collection, cancellationToken: ct);
            logger.LogWarning("Deleted collection {Collection} ({Points} points) for a rebuild", _qdrant.Collection, points);
            return points;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The chunk collection's name and point count, or null when it does not exist.</summary>
    public async Task<(string Collection, ulong Points)?> DescribeChunkCollectionAsync(CancellationToken ct) =>
        await client.CollectionExistsAsync(_qdrant.Collection, ct)
            ? (_qdrant.Collection, (await client.GetCollectionInfoAsync(_qdrant.Collection, ct)).PointsCount)
            : null;

    /// <summary>Adds any configured dense vector the collection lacks (embedding-model migration).</summary>
    public async Task AddMissingDenseVectorsAsync(CancellationToken ct)
    {
        var info = await client.GetCollectionInfoAsync(_qdrant.Collection, ct);
        var existing = info.Config.Params.VectorsConfig.ParamsMap?.Map.Keys.ToHashSet() ?? [];
        var missing = models.Profiles.Where(p => !existing.Contains(p.Key)).ToList();
        if (missing.Count == 0)
        {
            return;
        }
        // Qdrant cannot add a named vector to an existing collection via UpdateCollection's VectorParamsDiff
        // (that only changes HNSW/on-disk settings). Record the gap; migration requires the vector to exist.
        throw new InvalidOperationException(
            $"Collection '{_qdrant.Collection}' lacks dense vector(s) {string.Join(", ", missing.Select(m => m.Key))}. " +
            "A named vector cannot be added to an existing collection; rebuild it from the corpus with " +
            "`make rebuild-index FORCE=1` (DECISIONS.md §6). Nothing has been deleted.");
    }

    private async Task EnsureMetaCollectionAsync(CancellationToken ct)
    {
        if (!await client.CollectionExistsAsync(_qdrant.MetaCollection, ct))
        {
            await client.CreateCollectionAsync(_qdrant.MetaCollection, new VectorParams { Size = 1, Distance = Distance.Dot }, cancellationToken: ct);
        }
    }

    public async Task<IReadOnlySet<string>> DenseVectorNamesAsync(CancellationToken ct)
    {
        var info = await client.GetCollectionInfoAsync(_qdrant.Collection, ct);
        return info.Config.Params.VectorsConfig.ParamsMap?.Map.Keys.ToHashSet() ?? [];
    }
}
