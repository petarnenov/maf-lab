using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Retrieval.Configuration;
using Maf.Lab.Retrieval.Rerank;
using Maf.Lab.Retrieval.Search;
using Maf.Lab.Retrieval.Store;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.IntegrationTests;

/// <summary>
/// The relevance gate against the real indexed corpus, with a scripted judge standing in for Jev: the gate decides
/// whether a search answers at all and never which chunks it returns, one judgment serves gate and reranker, and a judge
/// that does not answer leaves the search exactly as it would be without the gate.
/// </summary>
[Collection(CorpusCollection.Name)]
public class RelevanceGateAcceptanceTests(CorpusIndexFixture corpus)
{
    private static readonly Principal AdvisorA = new("adam", TenantId.Firm("firm-a"), Role.ADVISOR, ["adv-a-1"]);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string Query = "what is the procedure when a fee schedule is missing";

    private static SearchSettings Settings(bool gate, bool rerank = false, string? reranker = null) =>
        new(RetrievalModes.Hybrid, FusionModes.Rrf, "dense_v3", rerank, RelevanceGate: gate, Reranker: reranker);

    private ServiceProvider Services(ScriptedJudge judge) =>
        corpus.Qdrant.Services(corpus.Collection, corpus.CorpusRoot, services: s => s.AddSingleton<IRelevanceJudge>(judge));

    [Fact]
    public async Task Nothing_judged_relevant_returns_no_results_and_the_rephrase_hint()
    {
        var judge = new ScriptedJudge(_ => 0.05);
        await using var services = Services(judge);
        var search = services.GetRequiredService<DocumentSearchService>();

        var outcome = await search.SearchAsync(AdvisorA, Query, null, 10, Settings(gate: true), Ct);

        Assert.Empty(outcome.Result.Results);
        Assert.Contains("No matching documentation", outcome.Result.RefineHint);
        Assert.Equal(1, judge.Calls);
    }

    [Fact]
    public async Task A_search_the_gate_passes_returns_exactly_what_fusion_returned()
    {
        await using var services = Services(new ScriptedJudge(i => i == 3 ? 0.9 : 0.1));
        var search = services.GetRequiredService<DocumentSearchService>();

        var gated = await search.SearchAsync(AdvisorA, Query, null, 10, Settings(gate: true), Ct);
        var ungated = await search.SearchAsync(AdvisorA, Query, null, 10, Settings(gate: false), Ct);

        Assert.NotEmpty(gated.Result.Results);
        Assert.Equal(ungated.Result.Results.Select(r => (r.DocId, r.SectionPath, r.Score)), gated.Result.Results.Select(r => (r.DocId, r.SectionPath, r.Score)));
    }

    [Fact]
    public async Task A_judge_that_does_not_answer_leaves_the_search_ungated()
    {
        await using var services = Services(new ScriptedJudge(_ => 0.0, fail: "timed out after 2s"));
        var search = services.GetRequiredService<DocumentSearchService>();
        var diagnostics = new SearchDiagnostics();

        var gated = await search.RankAsync(AdvisorA, Query, null, 20, Settings(gate: true), Ct, diagnostics);
        var ungated = await search.RankAsync(AdvisorA, Query, null, 20, Settings(gate: false), Ct);

        Assert.Equal(ungated.Select(c => c.Chunk.ChunkId), gated.Select(c => c.Chunk.ChunkId));
        Assert.Equal("timed out after 2s", diagnostics.Relevance!["reason"]!.GetValue<string>());
        Assert.False(diagnostics.Relevance["silenced"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Gate_off_and_no_jev_reranker_asks_nothing()
    {
        var judge = new ScriptedJudge(_ => 0.0);
        await using var services = Services(judge);
        var search = services.GetRequiredService<DocumentSearchService>();

        var outcome = await search.SearchAsync(AdvisorA, Query, null, 10, Settings(gate: false), Ct);

        Assert.NotEmpty(outcome.Result.Results);
        Assert.Equal(0, judge.Calls);
    }

    [Fact]
    public async Task Gate_and_jev_reranker_share_one_judgment()
    {
        // The last judged candidate is the most relevant, so the reranked order visibly differs from the fused one.
        var judge = new ScriptedJudge(i => i == 19 ? 0.95 : 0.4 - i * 0.01);
        await using var services = Services(judge);
        var search = services.GetRequiredService<DocumentSearchService>();

        var fused = await search.RankAsync(AdvisorA, Query, null, 20, Settings(gate: false), Ct);
        var diagnostics = new SearchDiagnostics();
        var ranked = await search.RankAsync(AdvisorA, Query, null, 20, Settings(gate: true, rerank: true, reranker: RerankerKinds.Jev), Ct, diagnostics);

        Assert.Equal(1, judge.Calls);
        Assert.Equal(fused.Count, ranked.Count);
        Assert.Equal(fused[Math.Min(19, fused.Count - 1)].Chunk.ChunkId, ranked[0].Chunk.ChunkId);
        Assert.Equal(fused.Select(c => c.Chunk.ChunkId).Order(), ranked.Select(c => c.Chunk.ChunkId).Order());
        Assert.Equal("jev", diagnostics.Settings["reranker"]!.GetValue<string>());
        Assert.True(diagnostics.Settings["relevanceGate"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Jev_rerank_of_a_search_that_found_nothing_returns_nothing_and_asks_nothing()
    {
        // Found by the retrieval eval: an off-domain query the dense floor already emptied, reranked by Jev.
        var judge = new ScriptedJudge(_ => 0.9);
        await using var services = Services(judge);
        var search = services.GetRequiredService<DocumentSearchService>();

        var ranked = await search.RankAsync(AdvisorA, Query, ["nonexistent-source-type"], 20,
            Settings(gate: true, rerank: true, reranker: RerankerKinds.Jev), Ct);

        Assert.Empty(ranked);
        Assert.Equal(0, judge.Calls);
    }

    [Fact]
    public async Task Diagnostics_of_a_silenced_search_still_list_what_was_found()
    {
        await using var services = Services(new ScriptedJudge(_ => 0.03));
        var search = services.GetRequiredService<DocumentSearchService>();
        var diagnostics = new SearchDiagnostics();

        var ranked = await search.RankAsync(AdvisorA, Query, null, 20, Settings(gate: true), Ct, diagnostics);

        Assert.Empty(ranked);
        Assert.NotEmpty(diagnostics.Fused);
        var relevance = diagnostics.Relevance!;
        Assert.True(relevance["silenced"]!.GetValue<bool>());
        Assert.Equal(0.03, relevance["max"]!.GetValue<double>());
        Assert.Equal(diagnostics.Fused.Count, relevance["scores"]!.AsArray().Count);
        Assert.All(relevance["scores"]!.AsArray(), s => Assert.Equal(0.03, s!["p"]!.GetValue<double>()));
    }

    [Fact]
    public async Task A_judged_search_returns_the_relevance_summary_without_diagnostics()
    {
        await using var services = Services(new ScriptedJudge(_ => 0.03));
        var search = services.GetRequiredService<DocumentSearchService>();

        var outcome = await search.SearchAsync(AdvisorA, Query, null, 10, Settings(gate: true), Ct);

        var summary = outcome.Relevance!;
        Assert.True(summary["gate"]!.GetValue<bool>());
        Assert.True(summary["silenced"]!.GetValue<bool>());
        Assert.False(summary["rerankedByJev"]!.GetValue<bool>());
        Assert.Equal(0.03, summary["max"]!.GetValue<double>());
        Assert.Equal("scripted", summary["model"]!.GetValue<string>());
        // Numbers and flags only: nothing that names a chunk, a document, the query or a passage.
        Assert.Equal(
            ["durationMs", "floor", "gate", "judged", "max", "model", "reason", "rerankedByJev", "reranker", "silenced"],
            summary.Select(kv => kv.Key).Order());
        Assert.DoesNotContain("fee schedule", summary.ToJsonString());
    }

    [Fact]
    public async Task The_summary_says_when_jev_ordered_the_results()
    {
        await using var services = Services(new ScriptedJudge(i => i == 0 ? 0.9 : 0.4));
        var search = services.GetRequiredService<DocumentSearchService>();

        var outcome = await search.SearchAsync(AdvisorA, Query, null, 10,
            Settings(gate: true, rerank: true, reranker: RerankerKinds.Jev), Ct);

        Assert.True(outcome.Relevance!["rerankedByJev"]!.GetValue<bool>());
        Assert.False(outcome.Relevance["silenced"]!.GetValue<bool>());
    }

    [Fact]
    public async Task A_search_that_asked_no_judge_has_no_summary()
    {
        await using var services = Services(new ScriptedJudge(_ => 0.9));
        var search = services.GetRequiredService<DocumentSearchService>();

        var outcome = await search.SearchAsync(AdvisorA, Query, null, 10, Settings(gate: false), Ct);

        Assert.Null(outcome.Relevance);
    }

    /// <summary>Answers each candidate by its fused position, and counts the requests.</summary>
    private sealed class ScriptedJudge(Func<int, double> score, string? fail = null) : IRelevanceJudge
    {
        private int _calls;

        public int Calls => _calls;

        public Task<RelevanceJudgement> JudgeAsync(string query, IReadOnlyList<ScoredChunk> candidates, CancellationToken ct)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(fail is not null
                ? new RelevanceJudgement(null, fail, "scripted", 2000)
                : new RelevanceJudgement(candidates.Select((_, i) => score(i)).ToList(), null, "scripted", 1));
        }
    }
}
