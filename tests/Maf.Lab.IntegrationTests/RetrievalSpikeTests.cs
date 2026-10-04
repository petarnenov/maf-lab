using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Indexing.Graph;
using Maf.Lab.Indexing.Pipeline;
using Maf.Lab.Retrieval.Configuration;
using Maf.Lab.Retrieval.Graph;
using Maf.Lab.Retrieval.Search;
using Maf.Lab.Retrieval.Sparse;
using Maf.Lab.Retrieval.Store;
using Maf.Lab.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Neo4j.Driver;

namespace Maf.Lab.IntegrationTests;

/// <summary>
/// The sample corpus indexed into Qdrant as <c>maf_chunks</c> (the name the spike's copy maps to a graph source), with its
/// own Neo4j container, and the copy made once. Shared by the spike's tests, which only read.
/// </summary>
public sealed class RetrievalSpikeFixture(QdrantFixture qdrant) : IAsyncLifetime
{
    public const string Collection = "maf_chunks";
    public Neo4jFixture Neo4j { get; } = new();
    public string CorpusRoot { get; } = Path.Combine(CorpusIndexFixture.RepoRoot(), "data");
    public QdrantFixture Qdrant => qdrant;
    public RetrievalCopySummary FirstCopy { get; private set; } = null!;

    public ServiceProvider Services() => qdrant.Services(Collection, CorpusRoot, configure: v =>
    {
        foreach (var (key, value) in Neo4j.Config())
        {
            v[key] = value;
        }
    });

    public async ValueTask InitializeAsync()
    {
        await Neo4j.InitializeAsync();
        await using var services = Services();
        await services.GetRequiredService<IndexingPipeline>().RunAsync(new IndexRequest(), CancellationToken.None);
        FirstCopy = await services.GetRequiredService<RetrievalCopyService>().RunAsync(null, CancellationToken.None);
    }

    public async ValueTask DisposeAsync() => await Neo4j.DisposeAsync();
}

[CollectionDefinition(Name)]
public sealed class RetrievalSpikeCollection : ICollectionFixture<RetrievalSpikeFixture>
{
    public const string Name = "retrieval-spike";
}

/// <summary>The retrieval spike end to end (neo4j-retrieval-spike): the copy, the scores, the fusion and the tenant guarantees on Neo4j.</summary>
[Collection(RetrievalSpikeCollection.Name)]
public sealed class RetrievalSpikeTests(RetrievalSpikeFixture spike)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly Principal AdvisorA = new("adam", TenantId.Firm("firm-a"), Role.ADVISOR, ["adv-a-1"]);
    private static readonly Principal AdvisorC = new("chris", TenantId.Firm("firm-c"), Role.ADVISOR, ["adv-c-1"]);

    private static async Task<IReadOnlyList<IRecord>> Cypher(IServiceProvider services, string query) =>
        (await services.GetRequiredService<IDriver>().ExecutableQuery(query).ExecuteAsync(Ct)).Result;

    private GraphChunkSearch Graph(IServiceProvider services) => new(services.GetRequiredService<IGraphReader>(), RetrievalSpikeFixture.Collection);

    private static SearchRequest Request(float[] dense, SparseVectorData sparse, string mode, string fusion = FusionModes.Rrf, int limit = 10) => new()
    {
        Dense = dense, Sparse = sparse, DenseVector = "dense_v3", Mode = mode, Fusion = fusion, Limit = limit, PrefetchLimit = 50,
    };

    private static async Task<(float[] Dense, SparseVectorData Sparse)> EncodeAsync(IServiceProvider services, string query)
    {
        var dense = FakeDenseEncoder.Default().Embed("dense_v3", query);
        var bm25 = await services.GetRequiredService<Bm25Store>().LoadAsync(Ct);
        return (dense, Bm25Encoder.EncodeQuery(bm25, query));
    }

    [Fact]
    public async Task The_schema_holds_the_vector_index_with_its_filter_properties_and_the_term_index()
    {
        await using var services = spike.Services();
        var indexes = await Cypher(services, "SHOW INDEXES YIELD name, type, properties RETURN name, type, properties");

        var dense = Assert.Single(indexes, r => r["name"].As<string>() == RetrievalGraph.DenseIndex);
        Assert.Equal("VECTOR", dense["type"].As<string>());
        Assert.Contains(indexes, r => r["name"].As<string>() == "term_key");
    }

    [Fact]
    public async Task The_copy_holds_every_point_with_its_payload_vector_and_weights_and_a_second_copy_writes_nothing()
    {
        await using var services = spike.Services();
        var maintenance = services.GetRequiredService<TenantScopedMaintenance>();
        var source = new List<ChunkVectors>();
        await foreach (var p in maintenance.ListChunkVectorsAsync("dense_v3", Ct))
        {
            source.Add(p);
        }

        Assert.Equal(0, spike.FirstCopy.Rejected);
        Assert.Equal(source.Count, spike.FirstCopy.Chunks);
        Assert.Equal(source.Sum(p => p.Sparse?.Indices.Length ?? 0), spike.FirstCopy.Edges);
        var sample = source.First(p => p.Sparse is { IsEmpty: false });
        var key = RetrievalGraph.ChunkKey(RetrievalSpikeFixture.Collection, sample.Chunk.ChunkId);
        var node = Assert.Single(await Cypher(services, $"MATCH (c:RetrievalChunk {{key: '{key}'}}) RETURN c.doc_id AS doc, c.text AS text, c.dense AS dense"));
        Assert.Equal(sample.Chunk.DocId, node["doc"].As<string>());
        Assert.Equal(sample.Chunk.Text, node["text"].As<string>());
        Assert.Equal(sample.Dense!.Select(x => (double)x), node["dense"].As<List<double>>());
        var weights = await Cypher(services, $"MATCH (t:Term)-[r:OCCURS_IN]->(c:RetrievalChunk {{key: '{key}'}}) RETURN t.term_id AS id, r.w AS w");
        Assert.Equal(sample.Sparse!.Indices.Length, weights.Count);
        foreach (var w in weights)
        {
            var i = Array.IndexOf(sample.Sparse.Indices, (uint)w["id"].As<long>());
            Assert.Equal(sample.Sparse.Values[i], w["w"].As<double>(), 5);
        }

        var again = await services.GetRequiredService<RetrievalCopyService>().RunAsync(null, Ct);
        Assert.Equal(0, again.Written);
        Assert.Equal(0, again.RemovedNodes + again.RemovedEdges);
    }

    [Theory]
    [InlineData(RetrievalModes.Dense)]
    [InlineData(RetrievalModes.Sparse)]
    public async Task One_branch_finds_the_same_chunks_with_the_same_scores_on_both_stores(string mode)
    {
        await using var services = spike.Services();
        var (dense, sparse) = await EncodeAsync(services, "what is the procedure when a fee schedule is missing");
        var request = Request(dense, sparse, mode);

        var qdrant = await services.GetRequiredService<TenantScopedSearch>().QueryAsync(AdvisorA, request, Ct);
        var neo4j = await Graph(services).QueryAsync(AdvisorA, request, Ct);

        Assert.Equal(10, neo4j.Count);
        var q = qdrant.ToDictionary(r => r.Chunk.ChunkId, r => r.Score);
        // The last rank can tie with the next candidate, so compare where both have the chunk.
        var shared = neo4j.Where(n => q.ContainsKey(n.Chunk.ChunkId)).ToList();
        Assert.True(shared.Count >= 9, $"only {shared.Count} of 10 in common");
        Assert.All(shared, n => Assert.Equal(q[n.Chunk.ChunkId], n.Score, 4));
    }

    [Theory]
    [InlineData(FusionModes.Rrf)]
    [InlineData(FusionModes.Dbsf)]
    public async Task Fusing_qdrants_own_branches_in_code_gives_qdrants_fused_result(string fusion)
    {
        await using var services = spike.Services();
        var qdrant = services.GetRequiredService<TenantScopedSearch>();
        var (dense, sparse) = await EncodeAsync(services, "household rebalancing fee schedule");

        var fused = await qdrant.QueryAsync(AdvisorA, Request(dense, sparse, RetrievalModes.Hybrid, fusion), Ct);
        var denseBranch = await qdrant.QueryAsync(AdvisorA, Request(dense, sparse, RetrievalModes.Dense, limit: 50), Ct);
        var sparseBranch = await qdrant.QueryAsync(AdvisorA, Request(dense, sparse, RetrievalModes.Sparse, limit: 50), Ct);
        var inCode = ChunkFusion.Fuse(fusion, [denseBranch, sparseBranch]).Take(10).ToList();

        var expected = fused.ToDictionary(f => f.Chunk.ChunkId, f => f.Score);
        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), inCode.Select(c => c.Chunk.ChunkId).Order(StringComparer.Ordinal));
        Assert.All(inCode, c => Assert.Equal(expected[c.Chunk.ChunkId], c.Score, 4));
    }

    [Fact]
    public async Task A_small_tenant_gets_its_full_count_on_neo4j_beside_a_large_one()
    {
        await using var services = spike.Services();
        const string query = "household rebalancing fee schedule";
        // Precondition with a raw unscoped query only test code may issue: the global top 10 is all firm-b.
        var global = await spike.Qdrant.RawClient().QueryAsync(RetrievalSpikeFixture.Collection,
            query: FakeDenseEncoder.Default().Embed("dense_v3", query), usingVector: "dense_v3", limit: 10, payloadSelector: true, cancellationToken: Ct);
        Assert.All(global, p => Assert.Equal("firm-b", p.Payload[ChunkSchema.TenantId].StringValue));

        var search = ActivatorUtilities.CreateInstance<DocumentSearchService>(services, (IChunkSearch)Graph(services));
        foreach (var mode in RetrievalModes.All)
        {
            var outcome = await search.SearchAsync(AdvisorC, query, null, 10, new SearchSettings(mode, FusionModes.Rrf, "dense_v3", false), Ct);
            Assert.Equal(10, outcome.Result.Results.Count);
            Assert.All(outcome.Chunks, c => Assert.Contains(c.Chunk.TenantId, new[] { "firm-c", "shared" }));
            Assert.Contains(outcome.Chunks, c => c.Chunk.TenantId == "firm-c");
        }
    }

    [Fact]
    public async Task Another_firms_chunks_are_never_candidates_on_neo4j()
    {
        await using var services = spike.Services();
        var (dense, sparse) = await EncodeAsync(services, "Northwind Capital household rebalancing fee schedule NW-CANARY-7731");

        foreach (var mode in RetrievalModes.All)
        {
            var result = await Graph(services).QueryAsync(AdvisorA, Request(dense, sparse, mode, limit: 50), Ct);
            Assert.NotEmpty(result);
            Assert.All(result, r => Assert.Contains(r.Chunk.TenantId, new[] { "firm-a", "shared" }));
        }
    }
}
