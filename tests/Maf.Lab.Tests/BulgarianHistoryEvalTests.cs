using Maf.Lab.Api.Agent;
using Maf.Lab.Eval.Datasets;
using Maf.Lab.Eval.Suites;

namespace Maf.Lab.Tests;

/// <summary>add-bulgarian-history-domain: the eval harness knows the fourth domain (tasks 5.1, 5.2).</summary>
public class BulgarianHistoryEvalTests
{
    private static string EvalsRoot => Path.Combine(CorpusLoaderTests.RepoRoot(), "evals");

    [Theory]
    [InlineData("bulgarian-history", true)]
    [InlineData("billing+bulgarian-history", true)]
    [InlineData("bulgarian-history+bulgarian-history", false)]
    public void Domain_expectation_accepts_the_new_domain(string expected, bool valid) =>
        Assert.Equal(valid, DatasetLoader.IsDomainExpectation(expected));

    [Fact]
    public void Label_names_the_domain_and_orders_crossings_as_Domains_All()
    {
        var one = DomainVerdict.From(new Dictionary<string, double> { ["bulgarian-history"] = 0.9 }, 0.5, 0.2);
        Assert.Equal("bulgarian-history", DomainSuite.Label(one));
        var two = DomainVerdict.From(new Dictionary<string, double> { ["bulgarian-history"] = 0.9, ["codebase"] = 0.8 }, 0.5, 0.2);
        Assert.Equal("codebase+bulgarian-history", DomainSuite.Label(two));
    }

    [Fact]
    public void Metrics_report_bulgarianHistoryRecall()
    {
        var hit = Metrics.Domain([("bulgarian-history", "bulgarian-history", "en", "design")]);
        Assert.Equal(1.0, hit["bulgarianHistoryRecall"]);
        var miss = Metrics.Domain([("bulgarian-history", "none", "en", "design")]);
        Assert.Equal(0.0, miss["bulgarianHistoryRecall"]);
    }

    [Fact]
    public void Tool_and_category_are_known_to_the_loader()
    {
        Assert.Contains("search_bulgarian_history", DatasetLoader.Tools);
        Assert.Contains("bulgarian-history", DatasetLoader.SelectionCategories);
    }

    [Fact]
    public void The_real_datasets_carry_the_new_rows()
    {
        Assert.True(DatasetLoader.Domain(EvalsRoot).Count(c => c.Expected == "bulgarian-history") >= 12);
        Assert.True(DatasetLoader.Selection(EvalsRoot).Count(c => c.Category == "bulgarian-history") >= 5);
    }
}
