using Maf.Lab.Retrieval.Configuration;
using Maf.Lab.Retrieval.Search;
using Maf.Lab.Retrieval.Sparse;
using Maf.Lab.Retrieval.Store;
using Qdrant.Client.Grpc;

namespace Maf.Lab.Tests;

/// <summary>
/// The floors ride on the candidate branches and never on the fused score. Fusion happens inside the store, so
/// this is the last place the two branches exist separately and the only place a floor can mean anything.
/// </summary>
public class RelevanceFloorTests
{
    private static readonly float[] Dense = [0.1f, 0.2f, 0.3f];
    private static readonly SparseVectorData Sparse = new([1u, 2u], [0.5f, 0.25f]);

    private static TenantScopedSearch.Plan Plan(float? denseFloor, float? sparseFloor, string mode = RetrievalModes.Hybrid) =>
        TenantScopedSearch.HybridPlan(
            TenantScopedSearch.DenseQuery(Dense),
            TenantScopedSearch.SparseQuery(Sparse),
            new SearchRequest
            {
                Dense = Dense,
                Sparse = Sparse,
                DenseVector = "dense_v1",
                Mode = mode,
                Limit = 5,
                DenseFloor = denseFloor,
                SparseFloor = sparseFloor,
            },
            new Filter(),
            100);

    [Fact]
    public void Each_branch_carries_its_own_floor()
    {
        var plan = Plan(0.55f, 2.5f);

        Assert.Equal(2, plan.Prefetch.Count);
        Assert.True(plan.Prefetch[0].HasScoreThreshold);
        Assert.Equal(0.55f, plan.Prefetch[0].ScoreThreshold, 5);
        Assert.True(plan.Prefetch[1].HasScoreThreshold);
        Assert.Equal(2.5f, plan.Prefetch[1].ScoreThreshold, 5);
    }

    [Fact]
    public void The_fused_query_never_carries_a_floor()
    {
        Assert.Null(Plan(0.55f, 2.5f).FusedFloor);
        Assert.Null(Plan(null, null).FusedFloor);
        Assert.Null(Plan(0.99f, null, RetrievalModes.Hybrid).FusedFloor);
    }

    [Fact]
    public void One_branch_may_have_a_floor_while_the_other_has_none()
    {
        var plan = Plan(0.55f, null);

        Assert.True(plan.Prefetch[0].HasScoreThreshold);
        Assert.False(plan.Prefetch[1].HasScoreThreshold);
    }

    [Fact]
    public void No_floors_builds_the_request_it_always_built()
    {
        var plan = Plan(null, null);

        Assert.Equal(2, plan.Prefetch.Count);
        Assert.All(plan.Prefetch, branch => Assert.False(branch.HasScoreThreshold));
        Assert.All(plan.Prefetch, branch => Assert.Equal(100ul, branch.Limit));
    }

    [Fact]
    public void Fusion_choice_is_unaffected_by_the_floors()
    {
        Assert.Equal(Fusion.Rrf, Plan(0.55f, 2.5f).Fusion);
        Assert.Equal(Fusion.Dbsf, TenantScopedSearch.HybridPlan(
            TenantScopedSearch.DenseQuery(Dense), TenantScopedSearch.SparseQuery(Sparse),
            new SearchRequest
            {
                Dense = Dense, Sparse = Sparse, DenseVector = "dense_v1",
                Fusion = FusionModes.Dbsf, Limit = 5, DenseFloor = 0.55f, SparseFloor = 2.5f,
            },
            new Filter(), 100).Fusion);
    }

    [Fact]
    public void Default_settings_take_the_floors_from_configuration()
    {
        var settings = new SearchSettings(RetrievalModes.Hybrid, FusionModes.Rrf, "dense_v1", false, 0.55f, 2.5f);

        Assert.Equal(0.55f, settings.DenseFloor);
        Assert.Equal(2.5f, settings.SparseFloor);
        // Explicit settings never inherit a floor; only DefaultSettings reads configuration.
        Assert.Null(new SearchSettings(RetrievalModes.Hybrid, FusionModes.Rrf, "dense_v1", false).DenseFloor);
        // The calibrated defaults: a dense floor the eval earned, and no sparse floor, because none qualified.
        Assert.Equal(0.65f, new RetrievalOptions().DenseFloor);
        Assert.Null(new RetrievalOptions().SparseFloor);
    }
}
