using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Retrieval.Configuration;
using Maf.Lab.Retrieval.Sparse;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using static Qdrant.Client.Grpc.Conditions;

namespace Maf.Lab.Retrieval.Store;

public sealed record SearchRequest
{
    public float[]? Dense { get; init; }
    public SparseVectorData? Sparse { get; init; }
    public required string DenseVector { get; init; }
    public string Mode { get; init; } = RetrievalModes.Hybrid;
    public string Fusion { get; init; } = FusionModes.Rrf;
    public IReadOnlyList<string>? SourceTypes { get; init; }
    public required int Limit { get; init; }
    public int PrefetchLimit { get; init; }
}

/// <summary>
/// THE query path. Every read of chunk content for a caller goes through <see cref="QueryAsync"/>,
/// which takes a <see cref="Principal"/> and scopes every prefetch branch and the outer query to the
/// principal's firm plus the shared corpus. There is deliberately no tenant parameter.
/// </summary>
public sealed class TenantScopedSearch(QdrantClient client, IOptions<QdrantOptions> options)
{
    private readonly string _collection = options.Value.Collection;

    public async Task<IReadOnlyList<ScoredChunk>> QueryAsync(Principal principal, SearchRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(principal);
        var filter = TenantFilter.For(principal, request.SourceTypes);
        var prefetchLimit = (ulong)Math.Max(request.PrefetchLimit, request.Limit * 5);

        var denseQuery = request.Dense is { Length: > 0 } dense ? Query(dense) : null;
        var sparseQuery = request.Sparse is { IsEmpty: false } sparse ? Query(sparse) : null;

        IReadOnlyList<ScoredPoint> points = request.Mode switch
        {
            RetrievalModes.Dense when denseQuery is not null =>
                await client.QueryAsync(_collection, query: denseQuery, usingVector: request.DenseVector, filter: filter,
                    limit: (ulong)request.Limit, payloadSelector: true, cancellationToken: ct),
            RetrievalModes.Sparse when sparseQuery is not null =>
                await client.QueryAsync(_collection, query: sparseQuery, usingVector: ChunkSchema.SparseVector, filter: filter,
                    limit: (ulong)request.Limit, payloadSelector: true, cancellationToken: ct),
            RetrievalModes.Hybrid => await HybridAsync(denseQuery, sparseQuery, request, filter, prefetchLimit, ct),
            _ => [],
        };

        return points.Select(p => new ScoredChunk(PayloadMapper.FromPayload(p.Payload), p.Score)).ToList();
    }

    private async Task<IReadOnlyList<ScoredPoint>> HybridAsync(
        Query? denseQuery, Query? sparseQuery, SearchRequest request, Filter filter, ulong prefetchLimit, CancellationToken ct)
    {
        var prefetch = new List<PrefetchQuery>();
        if (denseQuery is not null)
        {
            prefetch.Add(new PrefetchQuery { Query = denseQuery, Using = request.DenseVector, Filter = filter, Limit = prefetchLimit });
        }
        if (sparseQuery is not null)
        {
            prefetch.Add(new PrefetchQuery { Query = sparseQuery, Using = ChunkSchema.SparseVector, Filter = filter, Limit = prefetchLimit });
        }
        if (prefetch.Count == 0)
        {
            return [];
        }

        var fusion = request.Fusion == FusionModes.Dbsf ? Qdrant.Client.Grpc.Fusion.Dbsf : Qdrant.Client.Grpc.Fusion.Rrf;
        return await client.QueryAsync(_collection, query: fusion, prefetch: prefetch, filter: filter,
            limit: (ulong)request.Limit, payloadSelector: true, cancellationToken: ct);
    }

    private static Query Query(float[] dense) => new() { Nearest = new VectorInput { Dense = new DenseVector { Data = { dense } } } };

    private static Query Query(SparseVectorData sparse) => new()
    {
        Nearest = new VectorInput { Sparse = new Qdrant.Client.Grpc.SparseVector { Indices = { sparse.Indices }, Values = { sparse.Values } } },
    };
}

/// <summary>Builds the tenant filter. The only producer of tenant conditions in the codebase.</summary>
internal static class TenantFilter
{
    /// <summary>Principal-scoped: own firm OR shared.</summary>
    public static Filter For(Principal principal, IReadOnlyList<string>? sourceTypes = null)
    {
        var filter = new Filter
        {
            Must = { Match(ChunkSchema.TenantId, principal.ReadableTenants.Select(t => t.Value).ToList()) },
        };
        if (sourceTypes is { Count: > 0 })
        {
            filter.Must.Add(Match(ChunkSchema.SourceType, sourceTypes.ToList()));
        }
        return filter;
    }

    /// <summary>Single-tenant scope for maintenance of one tenant's own documents.</summary>
    public static Filter For(TenantId tenant) => new() { Must = { MatchKeyword(ChunkSchema.TenantId, tenant.Value) } };
}
