using Maf.Lab.Domain.Evals;
using Maf.Lab.Eval;
using Maf.Lab.Eval.Datasets;
using Maf.Lab.Eval.Suites;

namespace Maf.Lab.Tests;

/// <summary>The retrieval spike's comparison suite (neo4j-retrieval-spike): what it counts, and when it refuses.</summary>
public class RetrievalBackendsEvalTests
{
    [Fact]
    public void Overlap_is_the_share_of_neo4js_top_k_that_qdrant_also_ranks_there()
    {
        IReadOnlyList<string>[] qdrant = [["a", "b", "c", "d", "e"], ["x"], []];
        IReadOnlyList<string>[] neo4j = [["a", "b", "z", "d", "y"], ["x"], []];

        // Case 1: 3 of 5; case 2: 1 of 1; case 3 retrieved nothing on Neo4j and is left out.
        Assert.Equal((0.6 + 1.0) / 2, RetrievalBackendsSuite.Overlap(qdrant, neo4j, 5)!.Value, 9);
        Assert.Null(RetrievalBackendsSuite.Overlap([[]], [[]], 5));
    }

    [Fact]
    public void A_copy_that_does_not_match_its_collection_is_refused_by_name()
    {
        Assert.Null(RetrievalBackendsSuite.CopyMismatch("maf_chunks", 3330, 3330));
        var reason = RetrievalBackendsSuite.CopyMismatch("maf_chunks", 3330, 3200);
        Assert.Contains("maf_chunks", reason);
        Assert.Contains("make neo4j-chunks", reason);
    }

    [Fact]
    public void Scores_follow_the_retrieval_suite_answerable_and_off_domain_apart()
    {
        RetrievalCase[] cases =
        [
            new("r1", "q", ["a"], "firm-a", null, "en"),
            new("r2", "q", ["b"], "firm-a", null, "bg"),
            new("o1", "q", [], "firm-a", null, OffDomain: true),
        ];
        IReadOnlyList<string>[] ranked = [["a", "z"], ["z", "z2", "z3", "z4", "z5", "b"], []];

        var m = RetrievalBackendsSuite.Score(cases, ranked, "en", out var failures);

        Assert.Equal(0.5, m["recall@5"]);
        Assert.Equal(1.0, m["recall@20"]);
        Assert.Equal((1 + 1.0 / 6) / 2, m["mrr"], 9);
        Assert.Equal(1.0, m["offDomainSilence"]);
        Assert.Equal(0.0, m["recall@5:bg"]);
        Assert.Equal("r2", Assert.Single(failures).CaseId);
    }

    [Fact]
    public void It_is_a_comparison_never_in_all_and_never_baselined()
    {
        Assert.DoesNotContain(RetrievalBackendsSuite.Name, Program.SuitesOf("all"));
        Assert.Contains(RetrievalBackendsSuite.Name, Program.ComparisonSuites);
        var variant = new EvalVariantResult("neo4j-billing", new Dictionary<string, double> { ["recall@5"] = 0.1 }, new Dictionary<string, double>(), true, 1, []);
        Assert.Same(EvalBaseline.Empty, Program.AcceptInto(EvalBaseline.Empty, RetrievalBackendsSuite.Name, [variant], "r", DateTimeOffset.UnixEpoch));
    }
}
