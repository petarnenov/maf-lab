extern alias service;
using Maf.Lab.Api.Agent;
namespace Maf.Lab.Tests;
public sealed class DomainLabelTests
{
    // ---- the domain eval's labels --------------------------------------------------------------------------------------

    [Fact]
    public void Domain_labels_name_every_domain_in_scope_and_keep_both_for_billing_and_portfolio()
    {
        using var domains = StandInDomains.WithCodeDomain();
        DomainVerdict Verdict(params (string D, double P)[] ps) => DomainVerdict.From(ps.ToDictionary(p => p.D, p => p.P), 0.5, 0.2);

        Assert.Equal("codebase", service::Maf.Lab.Eval.Suites.DomainSuite.Label(Verdict(("codebase", 0.9))));
        Assert.Equal("both", service::Maf.Lab.Eval.Suites.DomainSuite.Label(Verdict(("portfolio", 0.9), ("billing", 0.8))));
        Assert.Equal("billing+codebase", service::Maf.Lab.Eval.Suites.DomainSuite.Label(Verdict(("codebase", 0.9), ("billing", 0.8))));
        Assert.True(Maf.Lab.Eval.Datasets.DatasetLoader.IsDomainExpectation("billing+codebase"));
        Assert.False(Maf.Lab.Eval.Datasets.DatasetLoader.IsDomainExpectation("billing+billing"));
        Assert.False(Maf.Lab.Eval.Datasets.DatasetLoader.IsDomainExpectation("weather"));

        var metrics = Maf.Lab.Eval.Suites.Metrics.Domain([("codebase", "codebase", "en", "design"), ("billing+codebase", "codebase", "en", "design")]);
        Assert.Equal(1.0, metrics["codebaseRecall"]);
        Assert.Equal(0.0, metrics["crossingRecall"]);
    }

}
