using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Retrieval.Configuration;
using Maf.Lab.Retrieval.Search;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.IntegrationTests;

/// <summary>
/// The floors against the real indexed corpus. Before they existed, an approximate-nearest-neighbour search
/// always returned its k nearest neighbours, so an empty result was unreachable and everything downstream that
/// waits for one — the refine hint, the zero-results signal, the review queue — was dead code.
/// </summary>
[Collection(CorpusCollection.Name)]
public class RelevanceFloorAcceptanceTests(CorpusIndexFixture corpus)
{
    private static readonly Principal AdvisorA = new("adam", TenantId.Firm("firm-a"), Role.ADVISOR, ["adv-a-1"]);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Floors no candidate in any corpus can clear, so the test is about the mechanism, not the numbers.</summary>
    private static SearchSettings Rejecting(string mode = RetrievalModes.Hybrid) =>
        new(mode, FusionModes.Rrf, "dense_v1", false, float.MaxValue, float.MaxValue);

    private static SearchSettings NoFloors(string mode = RetrievalModes.Hybrid) =>
        new(mode, FusionModes.Rrf, "dense_v1", false);

    [Fact]
    public async Task Nothing_above_the_floor_returns_no_results_and_a_hint_rather_than_an_error()
    {
        await using var services = corpus.Qdrant.Services(corpus.Collection, corpus.CorpusRoot);
        var search = services.GetRequiredService<DocumentSearchService>();

        var outcome = await search.SearchAsync(AdvisorA, "what is the procedure when a fee schedule is missing", null, 10,
            Rejecting(), Ct);

        Assert.Empty(outcome.Result.Results);
        Assert.Empty(outcome.Chunks);
        Assert.Equal(0, outcome.Result.TotalMatches);
        Assert.False(outcome.Result.Truncated);
        // Finding nothing is an answer: the caller is told how to ask better, not that something broke.
        Assert.Contains("No matching documentation", outcome.Result.RefineHint);
    }

    [Fact]
    public async Task Every_mode_can_return_nothing_once_its_branch_has_a_floor()
    {
        await using var services = corpus.Qdrant.Services(corpus.Collection, corpus.CorpusRoot);
        var search = services.GetRequiredService<DocumentSearchService>();

        foreach (var mode in RetrievalModes.All)
        {
            var outcome = await search.SearchAsync(AdvisorA, "how do I re-run a failed billing run", null, 10, Rejecting(mode), Ct);
            Assert.Empty(outcome.Result.Results);
            Assert.Contains("No matching documentation", outcome.Result.RefineHint);
        }
    }

    [Fact]
    public async Task Without_floors_the_same_search_returns_what_it_always_did()
    {
        await using var services = corpus.Qdrant.Services(corpus.Collection, corpus.CorpusRoot);
        var search = services.GetRequiredService<DocumentSearchService>();
        const string query = "what is the procedure when a fee schedule is missing";

        foreach (var mode in RetrievalModes.All)
        {
            var outcome = await search.SearchAsync(AdvisorA, query, null, 10, NoFloors(mode), Ct);
            Assert.NotEmpty(outcome.Result.Results);
            Assert.DoesNotContain("No matching documentation", outcome.Result.RefineHint ?? string.Empty);
        }
    }

    [Fact]
    public async Task A_floor_below_every_score_changes_nothing()
    {
        await using var services = corpus.Qdrant.Services(corpus.Collection, corpus.CorpusRoot);
        var search = services.GetRequiredService<DocumentSearchService>();
        const string query = "what does the FS-REQUIRED failure code mean";

        var without = await search.SearchAsync(AdvisorA, query, null, 10, NoFloors(), Ct);
        var withFloor = await search.SearchAsync(AdvisorA, query, null, 10,
            new SearchSettings(RetrievalModes.Hybrid, FusionModes.Rrf, "dense_v1", false, float.MinValue, float.MinValue), Ct);

        Assert.Equal(without.Result.Results.Select(r => r.DocId), withFloor.Result.Results.Select(r => r.DocId));
    }

    [Fact]
    public async Task The_floors_are_independent_per_branch()
    {
        await using var services = corpus.Qdrant.Services(corpus.Collection, corpus.CorpusRoot);
        var search = services.GetRequiredService<DocumentSearchService>();
        const string query = "how do I re-run a failed billing run";

        // Dense rejects everything, sparse rejects nothing: hybrid still answers, from the sparse branch alone.
        var outcome = await search.SearchAsync(AdvisorA, query, null, 10,
            new SearchSettings(RetrievalModes.Hybrid, FusionModes.Rrf, "dense_v1", false, float.MaxValue, null), Ct);

        Assert.NotEmpty(outcome.Result.Results);
    }

    [Fact]
    public async Task Diagnostics_show_the_near_misses_the_answer_was_denied()
    {
        await using var services = corpus.Qdrant.Services(corpus.Collection, corpus.CorpusRoot);
        var search = services.GetRequiredService<DocumentSearchService>();
        var diagnostics = new SearchDiagnostics();

        var ranked = await search.RankAsync(AdvisorA, "what is the procedure when a fee schedule is missing", null, 20,
            Rejecting(), Ct, diagnostics);

        // The answer gets nothing; the operator gets the whole story.
        Assert.Empty(ranked);
        Assert.NotEmpty(diagnostics.Dense);
        Assert.All(diagnostics.Dense, c => Assert.True(c!["belowFloor"]!.GetValue<bool>()));
        Assert.Equal(float.MaxValue, diagnostics.Settings["denseFloor"]!.GetValue<float>());
        Assert.Equal(float.MaxValue, diagnostics.Settings["sparseFloor"]!.GetValue<float>());
    }

    [Fact]
    public async Task Diagnostics_tell_all_dropped_apart_from_nothing_found()
    {
        await using var services = corpus.Qdrant.Services(corpus.Collection, corpus.CorpusRoot);
        var search = services.GetRequiredService<DocumentSearchService>();

        var allDropped = new SearchDiagnostics();
        await search.RankAsync(AdvisorA, "what is the procedure when a fee schedule is missing", null, 20, Rejecting(), Ct, allDropped);

        // A query whose terms reach no document of this tenant: the branches find nothing to begin with.
        var nothingFound = new SearchDiagnostics();
        await search.RankAsync(AdvisorA, "what is the procedure when a fee schedule is missing", ["nonexistent-source-type"], 20,
            NoFloors(), Ct, nothingFound);

        Assert.NotEmpty(allDropped.Dense);
        Assert.Empty(nothingFound.Dense);
    }

    [Fact]
    public async Task Without_floors_nothing_is_marked_as_dropped()
    {
        await using var services = corpus.Qdrant.Services(corpus.Collection, corpus.CorpusRoot);
        var search = services.GetRequiredService<DocumentSearchService>();
        var diagnostics = new SearchDiagnostics();

        await search.RankAsync(AdvisorA, "how do I re-run a failed billing run", null, 20, NoFloors(), Ct, diagnostics);

        Assert.NotEmpty(diagnostics.Dense);
        Assert.All(diagnostics.Dense, c => Assert.False(c!["belowFloor"]!.GetValue<bool>()));
        Assert.Null(diagnostics.Settings["denseFloor"]);
    }
}
