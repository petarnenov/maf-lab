using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Retrieval;
using Maf.Lab.Retrieval.Configuration;
using Maf.Lab.Retrieval.Graph;
using Maf.Lab.Retrieval.Sparse;
using Maf.Lab.Retrieval.Store;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Tests;

/// <summary>The retrieval spike's Neo4j chunk search (neo4j-retrieval-spike): scores, floors, fusion and where it may live.</summary>
public class GraphChunkSearchTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly Principal FirmA = new("u-a", TenantId.Firm("firm-a"), Role.ADVISOR, []);

    private static ChunkRecord Chunk(string id) => new()
    {
        TenantId = "firm-a", DocId = "d", ChunkId = id, SourceType = "docs", SourcePath = "p", SectionPath = "s",
        UpdatedAt = DateTimeOffset.UnixEpoch, ModelVersion = "m", Text = id, ContentHash = "h",
    };

    private static ScoredChunk S(string id, double score) => new(Chunk(id), score);

    private sealed class FakeGraph(IReadOnlyList<GraphChunkHit> dense, IReadOnlyList<GraphChunkHit> sparse) : IGraphReader
    {
        public List<object> Queries { get; } = [];

        public Task<TResult> ReadAsync<TResult>(Principal principal, GraphQuery<TResult> query, CancellationToken ct)
        {
            Queries.Add(query);
            object result = query is ChunkDenseSearch ? dense : sparse;
            return Task.FromResult((TResult)result);
        }
    }

    private static SearchRequest Request(string mode, float? denseFloor = null, float? sparseFloor = null, string fusion = FusionModes.Rrf) => new()
    {
        Dense = [0.1f, 0.2f],
        Sparse = new SparseVectorData([1, 2], [1f, 1f]),
        DenseVector = "dense_v3",
        Mode = mode,
        Fusion = fusion,
        Limit = 3,
        DenseFloor = denseFloor,
        SparseFloor = sparseFloor,
    };

    [Theory]
    [InlineData(1.0, 1.0)]
    [InlineData(0.5, 0.0)]
    [InlineData(0.8535534143447876, 0.7071068286895752)]
    public void Neo4js_normalised_cosine_converts_back_to_qdrants_scale(double neo4j, double cosine) =>
        Assert.Equal(cosine, GraphChunkSearch.CosineOf(neo4j), 6);

    [Fact]
    public async Task Each_branch_applies_its_own_floor_on_qdrants_scale_and_hybrid_fetches_five_times_the_count()
    {
        // Dense scores normalised: 0.9 → cos 0.8, 0.6 → cos 0.2. Floor 0.5 on the cosine keeps only "a".
        var graph = new FakeGraph([new(Chunk("a"), 0.9), new(Chunk("b"), 0.6)], [new(Chunk("c"), 3.0), new(Chunk("d"), 0.5)]);
        var search = new GraphChunkSearch(graph, "maf_chunks");

        var result = await search.QueryAsync(FirmA, Request(RetrievalModes.Hybrid, denseFloor: 0.5f, sparseFloor: 1f), Ct);

        Assert.Equal(["a", "c"], result.Select(r => r.Chunk.ChunkId).Order(StringComparer.Ordinal));
        Assert.All(graph.Queries.Cast<dynamic>(), q => Assert.Equal(15, (int)q.Count));
    }

    [Fact]
    public async Task A_search_whose_branches_are_all_below_their_floors_returns_nothing()
    {
        var graph = new FakeGraph([new(Chunk("a"), 0.6)], [new(Chunk("b"), 0.5)]);

        var result = await new GraphChunkSearch(graph, "maf_chunks").QueryAsync(FirmA, Request(RetrievalModes.Hybrid, 0.9f, 2f), Ct);

        Assert.Empty(result);
    }

    [Fact]
    public async Task A_single_mode_reads_one_branch_at_the_requested_count_and_keeps_its_scores()
    {
        var graph = new FakeGraph([new(Chunk("a"), 0.9)], []);

        var result = await new GraphChunkSearch(graph, "maf_chunks").QueryAsync(FirmA, Request(RetrievalModes.Dense), Ct);

        Assert.Equal(0.8, Assert.Single(result).Score, 6);
        Assert.Equal(3, ((ChunkDenseSearch)Assert.Single(graph.Queries)).Count);
    }

    [Fact]
    public void Rrf_uses_qdrants_constant_and_zero_based_ranks()
    {
        IReadOnlyList<ScoredChunk>[] branches = [[S("a", 0.9), S("b", 0.8)], [S("b", 5), S("c", 4)]];

        var fused = ChunkFusion.Rrf(branches);

        // b: 1/(2+1) + 1/(2+0) = 0.8333; a: 1/2 = 0.5; c: 1/3 = 0.3333.
        Assert.Equal(["b", "a", "c"], fused.Select(f => f.Chunk.ChunkId));
        Assert.Equal(1.0 / 3 + 0.5, fused[0].Score, 9);
        Assert.Equal(1.0 / 3, fused[2].Score, 9);
    }

    [Fact]
    public void Dbsf_remaps_each_branch_by_three_sigma_unclipped_and_a_flat_branch_gives_a_half()
    {
        IReadOnlyList<ScoredChunk>[] branches = [[S("a", 3), S("b", 1)], [S("b", 7), S("c", 7)]];

        var fused = ChunkFusion.Dbsf(branches).ToDictionary(f => f.Chunk.ChunkId, f => f.Score);

        // Branch 1: mean 2, sample sd √2; a → (3 − (2 − 3√2)) / 6√2, b → (1 − (2 − 3√2)) / 6√2. Branch 2 is flat: 0.5 each.
        var sd = Math.Sqrt(2);
        Assert.Equal((3 - (2 - 3 * sd)) / (6 * sd), fused["a"], 9);
        Assert.Equal((1 - (2 - 3 * sd)) / (6 * sd) + 0.5, fused["b"], 9);
        Assert.Equal(0.5, fused["c"], 9);
    }

    [Fact]
    public void Every_service_registers_the_qdrant_search_and_only_the_eval_builds_the_graph_one()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddMafRetrievalCore(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["JEV_MAF_LAB"] = "" }).Build());
        // The document search takes TenantScopedSearch itself; neither the interface nor the graph search is registered.
        Assert.Contains(services, d => d.ServiceType == typeof(TenantScopedSearch));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IChunkSearch) || d.ServiceType == typeof(GraphChunkSearch)
            || d.ImplementationType == typeof(GraphChunkSearch));

        // Constructed nowhere but the eval: the api, the MCP servers and the indexer never build it.
        var src = Path.Combine(CorpusLoaderTests.RepoRoot(), "src");
        var builders = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && File.ReadAllText(f).Contains("new GraphChunkSearch("))
            .Select(f => Path.GetRelativePath(src, f).Split(Path.DirectorySeparatorChar)[0])
            .Distinct().ToList();
        Assert.All(builders, project => Assert.Equal("Maf.Lab.Eval", project));
    }
}
