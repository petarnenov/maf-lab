using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Retrieval.Configuration;
using Maf.Lab.Retrieval.Rerank;
using Maf.Lab.Retrieval.Search;
using Maf.Lab.Retrieval.Store;
using Maf.Lab.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Qdrant.Client.Grpc;

namespace Maf.Lab.IntegrationTests;

[Collection(CorpusCollection.Name)]
public class TenancyAcceptanceTests(CorpusIndexFixture corpus)
{
    private static readonly Principal AdvisorA = new("adam", TenantId.Firm("firm-a"), Role.ADVISOR, ["adv-a-1"]);
    private static readonly Principal AdvisorC = new("chris", TenantId.Firm("firm-c"), Role.ADVISOR, ["adv-c-1"]);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Advisor_of_firm_a_receives_only_firm_a_and_shared_chunks()
    {
        await using var services = corpus.Qdrant.Services(corpus.Collection, corpus.CorpusRoot);
        var search = services.GetRequiredService<DocumentSearchService>();

        foreach (var mode in RetrievalModes.All)
        {
            var outcome = await search.SearchAsync(AdvisorA, "what is the procedure when a fee schedule is missing", null, 10,
                new SearchSettings(mode, FusionModes.Rrf, "dense_v1", false), Ct);
            Assert.NotEmpty(outcome.Chunks);
            Assert.All(outcome.Chunks, c => Assert.Contains(c.Chunk.TenantId, new[] { "firm-a", "shared" }));
            Assert.All(outcome.Result.Results, r => Assert.True(r.DocId.StartsWith("firm-a/") || r.DocId.StartsWith("shared/")));
        }
    }

    [Fact]
    public async Task Query_matching_many_firm_b_documents_returns_firm_a_full_count_without_leaking()
    {
        var logs = new CapturingLoggerProvider();
        var reranker = new RecordingReranker();
        await using var services = corpus.Qdrant.Services(corpus.Collection, corpus.CorpusRoot, logs: logs,
            services: s => s.AddSingleton<IReranker>(reranker));
        var search = services.GetRequiredService<DocumentSearchService>();
        const string query = "Northwind Capital household rebalancing fee schedule NW-CANARY-7731";

        foreach (var rerank in new[] { false, true })
        {
            var outcome = await search.SearchAsync(AdvisorA, query, null, 10, new SearchSettings(RetrievalModes.Hybrid, FusionModes.Rrf, "dense_v1", rerank), Ct);
            Assert.Equal(10, outcome.Result.Results.Count);
            Assert.All(outcome.Chunks, c => Assert.Contains(c.Chunk.TenantId, new[] { "firm-a", "shared" }));
            Assert.DoesNotContain(outcome.Result.Results, r => r.Snippet.Contains("NW-CANARY-7731-") || r.Snippet.Contains("Northwind"));
        }

        Assert.NotEmpty(reranker.Inputs);
        Assert.All(reranker.Inputs.SelectMany(i => i), c => Assert.Contains(c.Chunk.TenantId, new[] { "firm-a", "shared" }));
        Assert.DoesNotContain(logs.Messages, m => m.Contains("NW-CANARY") || m.Contains("Northwind") || m.Contains(query));
    }

    [Fact]
    public async Task Small_tenant_gets_its_top_10_even_when_global_neighbours_are_all_in_the_large_tenant()
    {
        await using var services = corpus.Qdrant.Services(corpus.Collection, corpus.CorpusRoot);
        const string query = "household rebalancing fee schedule";
        var encoder = FakeDenseEncoder.Default();

        // Precondition, using a raw unscoped query that only test code may issue: the global top-10 is all firm-b.
        var global = await corpus.Qdrant.RawClient().QueryAsync(corpus.Collection, query: encoder.Embed("dense_v1", query),
            usingVector: "dense_v1", limit: 10, payloadSelector: true, cancellationToken: Ct);
        Assert.All(global, p => Assert.Equal("firm-b", p.Payload[ChunkSchema.TenantId].StringValue));

        var search = services.GetRequiredService<DocumentSearchService>();
        foreach (var mode in RetrievalModes.All)
        {
            var outcome = await search.SearchAsync(AdvisorC, query, null, 10, new SearchSettings(mode, FusionModes.Rrf, "dense_v1", false), Ct);
            Assert.Equal(10, outcome.Result.Results.Count);
            Assert.All(outcome.Chunks, c => Assert.Contains(c.Chunk.TenantId, new[] { "firm-c", "shared" }));
            Assert.Contains(outcome.Chunks, c => c.Chunk.TenantId == "firm-c");
        }
    }

    private sealed class RecordingReranker : IReranker
    {
        public List<IReadOnlyList<ScoredChunk>> Inputs { get; } = [];

        public Task<IReadOnlyList<ScoredChunk>> RerankAsync(string query, IReadOnlyList<ScoredChunk> candidates, CancellationToken ct)
        {
            Inputs.Add(candidates);
            return Task.FromResult<IReadOnlyList<ScoredChunk>>(candidates.Reverse().ToList());
        }
    }
}
