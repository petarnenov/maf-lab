using System.Text.Json;
using Maf.Lab.TestGen;
using Maf.Lab.TestGen.Coverage;

namespace Maf.Lab.Tests;

/// <summary>A related run's target coverage, combined with the baseline's (focus-test-runs-on-the-target).</summary>
public sealed class FocusedCoverageTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly LineHits Baseline = new([1, 2, 3, 4], [5, 6, 7, 8, 9, 10]);

    private static RunnerResult Run(string scope, LineHits? lines, double? pct, string build = BuildOutcome.Ok) =>
        new(RunnerStatus.Ok, build, [], new TestCounts(3, 0, 0), [], null, "/work", pct, [], 1,
            new TestSelection(scope, ["tests/Maf.Lab.Tests/CalcTests.cs"]), lines);

    [Fact]
    public void A_related_run_counts_what_the_baseline_covered()
    {
        // The focused run covered 5–7 itself, and 4 again; 20 is not an executable line of the file at the baseline.
        var focused = Run(TestScope.Related, new LineHits([4, 5, 6, 7, 20], [1, 2, 3, 8, 9, 10]), 40);

        var merged = FocusedCoverage.Apply(focused, Baseline);

        Assert.Equal(70, merged.TargetPct);
        Assert.Equal([[8, 10]], merged.Uncovered);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7], merged.TargetLines!.Covered);
        Assert.Equal([8, 9, 10], merged.TargetLines.Uncovered);
    }

    [Fact]
    public void A_focused_report_without_the_target_leaves_the_baseline()
    {
        var merged = FocusedCoverage.Apply(Run(TestScope.Related, null, 0), Baseline);

        Assert.Equal(40, merged.TargetPct);
        Assert.Equal([[5, 10]], merged.Uncovered);
    }

    [Fact]
    public void A_whole_run_an_unmeasured_run_or_no_baseline_is_left_as_it_is()
    {
        var whole = Run(TestScope.All, new LineHits([5], [1, 2, 3, 4, 6, 7, 8, 9, 10]), 10);
        var broken = Run(TestScope.Related, null, null, BuildOutcome.Failed);
        var related = Run(TestScope.Related, new LineHits([5], []), 100);

        Assert.Same(whole, FocusedCoverage.Apply(whole, Baseline));
        Assert.Same(broken, FocusedCoverage.Apply(broken, Baseline));
        Assert.Same(related, FocusedCoverage.Apply(related, null));
    }

    [Fact]
    public void The_percentage_is_rounded_as_the_dashboard_rounds_it()
    {
        var merged = FocusedCoverage.Apply(Run(TestScope.Related, new LineHits([2], [1, 3]), 33.3), new LineHits([], [1, 2, 3]));

        Assert.Equal(FileCoverage.Pct(1, 3), merged.TargetPct);
        Assert.Equal([[1, 1], [3, 3]], merged.Uncovered);
    }

    [Fact]
    public void Lines_listed_twice_are_covered_when_either_ran()
    {
        var hits = FocusedCoverage.Of([new LineCoverage(2, 0, 0, 0), new LineCoverage(1, 0, 0, 0), new LineCoverage(2, 3, 0, 0)]);

        Assert.Equal([2], hits.Covered);
        Assert.Equal([1], hits.Uncovered);
    }

    [Fact]
    public void Older_requests_and_results_still_read()
    {
        var request = JsonSerializer.Deserialize<RunnerRequest>("""{"commit":"abcdef1","toolchain":"dotnet"}""", Json)!;
        var result = JsonSerializer.Deserialize<RunnerResult>("""
            {"status":"ok","build":"ok","diagnostics":[],"tests":{"passed":1,"failed":0,"skipped":0},"failures":[],
             "targetPct":50,"uncovered":[[3,4]],"durationMs":5}
            """, Json)!;
        var sent = JsonSerializer.Deserialize<RunnerRequest>(
            JsonSerializer.Serialize(new RunnerRequest("abcdef1", "vitest", null, "web/src/a.ts", TestScope.Related), Json), Json)!;

        Assert.Null(request.Tests);
        Assert.Null(result.Selection);
        Assert.Null(result.TargetLines);
        Assert.False(result.Focused);
        Assert.Equal(TestScope.Related, sent.Tests);
    }
}
