using Maf.Lab.Domain.Evals;
using Maf.Lab.Eval.Reports;

namespace Maf.Lab.Tests;

public class RepeatedRunsTests
{
    private static EvalVariantResult Run(double faithfulness, string? failed = null, double? agreement = null)
    {
        var metrics = new Dictionary<string, double> { ["faithfulness"] = faithfulness };
        if (agreement is { } a)
        {
            metrics["jevGroundedAgreement"] = a;
        }
        return new EvalVariantResult("agent", metrics, new Dictionary<string, double> { ["faithfulness"] = 0.9 }, true, 36,
            failed is null ? [] : [new EvalCaseFailure(failed, "f=0.5")]);
    }

    [Fact]
    public void Three_runs_read_as_their_mean_with_the_thresholds_reapplied_and_every_failure_marked_with_its_run()
    {
        var mean = Assert.Single(RepeatedRuns.Mean([[Run(0.98)], [Run(0.903, "g-14")], [Run(0.96, "g-02", agreement: 1)]]));

        Assert.Equal(0.9477, mean.Metrics["faithfulness"]);
        Assert.True(mean.Passed);
        Assert.Equal(["[r2] f=0.5", "[r3] f=0.5"], mean.Failures.Select(f => f.Reason));
        Assert.Equal(["g-14", "g-02"], mean.Failures.Select(f => f.CaseId));
        // A metric only some runs report is averaged over those runs, not counted as zero in the others.
        Assert.Equal(1, mean.Metrics["jevGroundedAgreement"]);
    }

    [Fact]
    public void A_mean_below_the_threshold_fails_even_when_one_run_passed()
    {
        var mean = Assert.Single(RepeatedRuns.Mean([[Run(0.95)], [Run(0.85)], [Run(0.86)]]));

        Assert.False(mean.Passed);
    }

    [Fact]
    public void A_single_run_is_returned_unchanged()
    {
        IReadOnlyList<EvalVariantResult> one = [Run(0.9, "g-1")];

        Assert.Same(one, RepeatedRuns.Mean([one]));
    }
}
