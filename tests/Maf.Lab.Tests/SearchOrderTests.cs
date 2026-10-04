using Maf.Lab.Retrieval.Store;

namespace Maf.Lab.Tests;

/// <summary>
/// The order a search returns (stabilize-tied-search-order): score first, then chunk id, so equal RRF scores — which
/// rank-only fusion produces routinely — come back the same way every time, and so does the tie at the limit.
/// </summary>
public class SearchOrderTests
{
    private static ScoredChunk Chunk(string id, double score) => new(new ChunkRecord
    {
        TenantId = "firm-a", DocId = "firm-a/docs/d.md", ChunkId = id, SourceType = "docs", SourcePath = "docs/d.md",
        SectionPath = id, UpdatedAt = DateTimeOffset.UnixEpoch, ModelVersion = "m", Text = "t", ContentHash = "h",
    }, score);

    private static IEnumerable<string> Ids(IEnumerable<ScoredChunk> results) => results.Select(r => r.Chunk.ChunkId);

    // 1/(60+3): what RRF gives a candidate found by one branch only, at rank 3 — the same whichever branch found it.
    private const double Rank3 = 1.0 / 63;

    [Fact]
    public void Equal_scores_are_ordered_by_chunk_id_whatever_order_the_store_returned()
    {
        var a = Chunk("doc#a", Rank3);
        var b = Chunk("doc#b", Rank3);
        var c = Chunk("doc#c", Rank3);

        Assert.Equal(["doc#a", "doc#b", "doc#c"], Ids(TenantScopedSearch.Settle([c, a, b], 10)));
        Assert.Equal(["doc#a", "doc#b", "doc#c"], Ids(TenantScopedSearch.Settle([b, c, a], 10)));
    }

    [Fact]
    public void A_higher_score_always_comes_first()
    {
        var results = TenantScopedSearch.Settle([Chunk("doc#a", 0.01), Chunk("doc#z", 0.03), Chunk("doc#m", 0.02)], 10);

        Assert.Equal(["doc#z", "doc#m", "doc#a"], Ids(results));
    }

    [Fact]
    public void The_tie_at_the_limit_keeps_the_lowest_chunk_id()
    {
        var top = Chunk("doc#top", 0.05);

        var one = TenantScopedSearch.Settle([top, Chunk("doc#y", Rank3), Chunk("doc#x", Rank3)], 2);
        var two = TenantScopedSearch.Settle([Chunk("doc#x", Rank3), top, Chunk("doc#y", Rank3)], 2);

        Assert.Equal(["doc#top", "doc#x"], Ids(one));
        Assert.Equal(Ids(one), Ids(two));
    }

    [Fact]
    public void Chunk_ids_compare_ordinally_not_by_culture()
    {
        var results = TenantScopedSearch.Settle([Chunk("doc#b", Rank3), Chunk("doc#B", Rank3), Chunk("doc#a", Rank3)], 10);

        Assert.Equal(["doc#B", "doc#a", "doc#b"], Ids(results));
    }

    [Fact]
    public void Fewer_results_than_the_limit_are_returned_whole()
    {
        Assert.Equal(2, TenantScopedSearch.Settle([Chunk("doc#a", 0.1), Chunk("doc#b", 0.2)], 10).Count);
        Assert.Empty(TenantScopedSearch.Settle([], 10));
    }
}
