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
    /// <summary>
    /// Lowest score a candidate may have on each branch and still count. Null leaves that branch unfiltered.
    /// Never applied to a fused score: RRF expresses rank and how many candidates were fused, not closeness.
    /// </summary>
    public float? DenseFloor { get; init; }
    public float? SparseFloor { get; init; }
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
                    limit: (ulong)request.Limit, scoreThreshold: request.DenseFloor, payloadSelector: true, cancellationToken: ct),
            RetrievalModes.Sparse when sparseQuery is not null =>
                await client.QueryAsync(_collection, query: sparseQuery, usingVector: ChunkSchema.SparseVector, filter: filter,
                    limit: (ulong)request.Limit, scoreThreshold: request.SparseFloor, payloadSelector: true, cancellationToken: ct),
            RetrievalModes.Hybrid => await HybridAsync(denseQuery, sparseQuery, request, filter, prefetchLimit, ct),
            _ => [],
        };

        return points.Select(p => new ScoredChunk(PayloadMapper.FromPayload(p.Payload), p.Score)).ToList();
    }

    private async Task<IReadOnlyList<ScoredPoint>> HybridAsync(
        Query? denseQuery, Query? sparseQuery, SearchRequest request, Filter filter, ulong prefetchLimit, CancellationToken ct)
    {
        var plan = HybridPlan(denseQuery, sparseQuery, request, filter, prefetchLimit);
        if (plan.Prefetch.Count == 0)
        {
            return [];
        }
        return await client.QueryAsync(_collection, query: plan.Fusion, prefetch: plan.Prefetch, filter: filter,
            limit: (ulong)request.Limit, scoreThreshold: plan.FusedFloor, payloadSelector: true, cancellationToken: ct);
    }

    /// <summary>
    /// What a hybrid search asks the store for. Separated from the call so the invariant that matters can be
    /// asserted: the floors ride on the branches, and <see cref="HybridPlan.FusedFloor"/> is always null.
    /// </summary>
    internal sealed record Plan(IReadOnlyList<PrefetchQuery> Prefetch, Fusion Fusion, float? FusedFloor);

    internal static Plan HybridPlan(Query? denseQuery, Query? sparseQuery, SearchRequest request, Filter filter, ulong prefetchLimit)
    {
        var prefetch = new List<PrefetchQuery>();
        if (denseQuery is not null)
        {
            prefetch.Add(Branch(denseQuery, request.DenseVector, filter, prefetchLimit, request.DenseFloor));
        }
        if (sparseQuery is not null)
        {
            prefetch.Add(Branch(sparseQuery, ChunkSchema.SparseVector, filter, prefetchLimit, request.SparseFloor));
        }
        var fusion = request.Fusion == FusionModes.Dbsf ? Fusion.Dbsf : Fusion.Rrf;
        // Null, always. An RRF score is a function of rank and of how many candidates were fused, so it says
        // nothing about closeness to the query and there is no floor that could mean anything against it.
        return new Plan(prefetch, fusion, null);
    }

    /// <summary>One candidate branch, carrying its own floor so a candidate too far from the query never fuses.</summary>
    private static PrefetchQuery Branch(Query query, string vector, Filter filter, ulong limit, float? floor)
    {
        var branch = new PrefetchQuery { Query = query, Using = vector, Filter = filter, Limit = limit };
        if (floor is { } threshold)
        {
            branch.ScoreThreshold = threshold;
        }
        return branch;
    }

    /// <summary>The store's query for a dense vector, exposed so a test can build the same plan the search does.</summary>
    internal static Query DenseQuery(float[] dense) => Query(dense);

    internal static Query SparseQuery(SparseVectorData sparse) => Query(sparse);

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
