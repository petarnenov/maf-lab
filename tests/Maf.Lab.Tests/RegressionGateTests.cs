using System.Text.Json;
using Maf.Lab.Domain.Evals;
using Maf.Lab.Eval;
using Maf.Lab.Eval.Reports;
using Microsoft.Extensions.Configuration;

namespace Maf.Lab.Tests;

/// <summary>
/// The gate that catches getting worse, as opposed to the thresholds that catch being bad. Every status matters:
/// a metric nobody compares is a gate that has quietly stopped gating.
/// </summary>
public class RegressionGateTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static EvalVariantResult Variant(string name, params (string Metric, double Value)[] metrics) =>
        new(name, metrics.ToDictionary(m => m.Metric, m => m.Value), new Dictionary<string, double>(), true, 10, []);

    private static SuiteBaseline Baseline(string variant, params (string Metric, double Value)[] metrics) =>
        new(new Dictionary<string, IReadOnlyDictionary<string, double>>
        {
            [variant] = metrics.ToDictionary(m => m.Metric, m => m.Value),
        }, "20260920-000000-retrieval", DateTimeOffset.UtcNow);

    private static MetricComparison Of(IReadOnlyList<MetricComparison> comparisons, string metric) =>
        comparisons.Single(c => c.Metric == metric);

    [Fact]
    public void A_drop_beyond_the_tolerance_is_a_regression_and_one_within_it_is_noise()
    {
        var baseline = Baseline("hybrid", ("recall@5", 0.687), ("recall@5:bg", 0.681), ("mrr", 0.635));
        var run = Variant("hybrid", ("recall@5", 0.670), ("recall@5:bg", 0.420), ("mrr", 0.635));

        var comparisons = RegressionGate.Compare(baseline, [run], tolerance: 0.02);

        Assert.Equal(MetricStatus.Noise, Of(comparisons, "recall@5").Status); // -0.017, within tolerance
        Assert.Equal(MetricStatus.Regression, Of(comparisons, "recall@5:bg").Status);
        Assert.Equal(-0.261, Of(comparisons, "recall@5:bg").Delta!.Value, 3);
        Assert.Equal(MetricStatus.Improvement, Of(comparisons, "mrr").Status); // unchanged counts as not worse
        Assert.True(RegressionGate.HasRegression(comparisons));

        var line = Assert.Single(RegressionGate.Describe(comparisons, "retrieval"), l => l.StartsWith('✗'));
        Assert.Contains("retrieval/hybrid recall@5:bg", line);
        Assert.Contains("0.681", line);
        Assert.Contains("0.42", line);
        Assert.Contains("-0.261", line);
    }

    [Fact]
    public void A_drop_exactly_at_the_tolerance_is_not_a_regression()
    {
        var comparisons = RegressionGate.Compare(Baseline("agent", ("recall", 1.0)), [Variant("agent", ("recall", 0.98))], 0.02);

        Assert.Equal(MetricStatus.Noise, Of(comparisons, "recall").Status);
        Assert.False(RegressionGate.HasRegression(comparisons));
    }

    [Fact]
    public void An_improvement_never_fails_and_is_reported_as_a_gain()
    {
        var comparisons = RegressionGate.Compare(Baseline("hybrid", ("recall@5", 0.456)), [Variant("hybrid", ("recall@5", 0.687))], 0.02);

        Assert.Equal(MetricStatus.Improvement, Of(comparisons, "recall@5").Status);
        Assert.False(RegressionGate.HasRegression(comparisons));
        Assert.Contains("↑ improved", Assert.Single(RegressionGate.Describe(comparisons, "retrieval")));
    }

    [Fact]
    public void A_metric_the_baseline_does_not_know_is_new_not_ignored()
    {
        var comparisons = RegressionGate.Compare(Baseline("hybrid", ("recall@5", 0.687)),
            [Variant("hybrid", ("recall@5", 0.687), ("recall@5:bg", 0.681))], 0.02);

        Assert.Equal(MetricStatus.New, Of(comparisons, "recall@5:bg").Status);
        Assert.False(RegressionGate.HasRegression(comparisons)); // new metrics do not fail a run
        Assert.Contains("new metric", string.Join("\n", RegressionGate.Describe(comparisons, "retrieval")));
    }

    [Fact]
    public void A_baseline_metric_the_run_did_not_produce_is_missing()
    {
        // The other half of a rename: the old key is orphaned while the new one has nothing to compare against.
        var comparisons = RegressionGate.Compare(Baseline("hybrid", ("recall@5", 0.687), ("recall_at_5", 0.687)),
            [Variant("hybrid", ("recall@5", 0.687))], 0.02);

        Assert.Equal(MetricStatus.Missing, Of(comparisons, "recall_at_5").Status);
        Assert.Null(Of(comparisons, "recall_at_5").Value);
        Assert.Contains("missing", string.Join("\n", RegressionGate.Describe(comparisons, "retrieval")));
    }

    [Fact]
    public void Without_a_baseline_everything_is_new_and_nothing_fails()
    {
        var comparisons = RegressionGate.Compare(null, [Variant("hybrid", ("recall@5", 0.687))], 0.02);

        Assert.Equal(MetricStatus.New, Assert.Single(comparisons).Status);
        Assert.False(RegressionGate.HasRegression(comparisons));
    }

    [Fact]
    public async Task A_missing_baseline_file_reads_as_empty_and_a_round_trip_keeps_values_and_provenance()
    {
        var root = Directory.CreateTempSubdirectory("maf-baseline-").FullName;
        Assert.Empty((await BaselineStore.ReadAsync(root, Ct)).Suites);

        var accepted = BaselineStore.FromVariants(
            [Variant("hybrid", ("recall@5", 0.687), ("mrr", 0.635)), Variant("dense", ("recall@5", 0.643))],
            "20260920-101112-retrieval", new DateTimeOffset(2026, 9, 20, 10, 11, 12, TimeSpan.Zero));
        await BaselineStore.WriteAsync(root, BaselineStore.With(EvalBaseline.Empty, "retrieval", accepted), Ct);

        var read = await BaselineStore.ReadAsync(root, Ct);
        var suite = read.Suites["retrieval"];
        Assert.Equal("20260920-101112-retrieval", suite.AcceptedFrom);
        Assert.Equal(new DateTimeOffset(2026, 9, 20, 10, 11, 12, TimeSpan.Zero), suite.AcceptedAt);
        Assert.Equal(0.687, suite.Metrics["hybrid"]["recall@5"]);
        Assert.Equal(0.643, suite.Metrics["dense"]["recall@5"]);
    }

    [Fact]
    public void A_run_below_its_thresholds_is_not_accepted()
    {
        var failing = new EvalVariantResult("hybrid", new Dictionary<string, double> { ["recall@5"] = 0.4 },
            new Dictionary<string, double> { ["recall@5"] = 0.6 }, false, 10, []);

        Assert.Null(BaselineStore.Accept(EvalBaseline.Empty, "retrieval", [failing], "r1", DateTimeOffset.UtcNow));
        Assert.Null(BaselineStore.Accept(EvalBaseline.Empty, "retrieval", [], "r1", DateTimeOffset.UtcNow));

        var passing = Variant("hybrid", ("recall@5", 0.687));
        var accepted = BaselineStore.Accept(EvalBaseline.Empty, "retrieval", [passing], "r2", DateTimeOffset.UtcNow);
        Assert.Equal(0.687, accepted!.Suites["retrieval"].Metrics["hybrid"]["recall@5"]);
        Assert.Equal("r2", accepted.Suites["retrieval"].AcceptedFrom);
    }

    [Fact]
    public async Task A_report_carries_its_comparison_and_an_older_report_without_one_still_reads()
    {
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var dir = Directory.CreateTempSubdirectory("maf-report-").FullName;
        var comparison = new MetricComparison("hybrid", "recall@5:bg", 0.681, 0.42, -0.261, MetricStatus.Regression);
        var report = new EvalReport("20260920-000000-retrieval", "retrieval", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
            new Dictionary<string, string>(), [Variant("hybrid", ("recall@5:bg", 0.42))], false, [comparison]);
        var path = await ReportWriter.WriteAsync(dir, report, Ct);

        var written = JsonSerializer.Deserialize<EvalReport>(await File.ReadAllTextAsync(path, Ct), json)!;
        Assert.Equal(MetricStatus.Regression, Assert.Single(written.Comparisons!).Status);
        Assert.Equal(-0.261, Assert.Single(written.Comparisons!).Delta!.Value, 3);

        // Reports written before the gate existed carry no comparisons at all.
        var older = JsonSerializer.Deserialize<EvalReport>(
            OldReportJson, json)!;
        Assert.Null(older.Comparisons);
        Assert.True(older.Passed);
    }

    private const string OldReportJson =
        """{"runId":"r","suite":"retrieval","startedAt":"2026-09-19T00:00:00Z","finishedAt":"2026-09-19T00:00:01Z","settings":{},"variants":[],"passed":true}""";

    [Fact]
    public void The_tolerance_is_configuration_with_a_default()
    {
        Assert.Equal(0.02, new EvalOptions().RegressionTolerance);

        var configured = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Evals:RegressionTolerance"] = "0.005" })
            .Build()
            .GetSection(EvalOptions.Section)
            .Get<EvalOptions>()!;
        Assert.Equal(0.005, configured.RegressionTolerance);

        // A suite whose measured noise exceeds the default gets its own, and the others keep it.
        var perSuite = new EvalOptions { RegressionTolerance = 0.02, RegressionTolerances = { ["retrieval"] = 0.03 } };
        Assert.Equal(0.03, perSuite.ToleranceFor("retrieval"));
        Assert.Equal(0.02, perSuite.ToleranceFor("selection"));

        // The same drop is noise under one tolerance and a regression under the other.
        var baseline = Baseline("hybrid", ("recall@5", 0.69));
        var run = new[] { Variant("hybrid", ("recall@5", 0.68)) };
        Assert.Equal(MetricStatus.Noise, RegressionGate.Compare(baseline, run, 0.02)[0].Status);
        Assert.Equal(MetricStatus.Regression, RegressionGate.Compare(baseline, run, 0.005)[0].Status);
    }

    [Fact]
    public async Task Accepting_one_suite_leaves_the_others_alone_and_the_file_is_written_in_a_stable_order()
    {
        var root = Directory.CreateTempSubdirectory("maf-baseline-").FullName;
        var at = DateTimeOffset.UtcNow;
        var first = BaselineStore.With(EvalBaseline.Empty, "selection",
            BaselineStore.FromVariants([Variant("agent", ("recall", 1.0))], "r1", at));
        await BaselineStore.WriteAsync(root, first, Ct);

        var withRetrieval = BaselineStore.With(await BaselineStore.ReadAsync(root, Ct), "retrieval",
            BaselineStore.FromVariants([Variant("hybrid", ("mrr", 0.635), ("recall@5", 0.687))], "r2", at));
        await BaselineStore.WriteAsync(root, withRetrieval, Ct);

        var text = await File.ReadAllTextAsync(BaselineStore.PathFor(root), Ct);
        var read = await BaselineStore.ReadAsync(root, Ct);
        Assert.Equal(1.0, read.Suites["selection"].Metrics["agent"]["recall"]);
        Assert.Equal(0.687, read.Suites["retrieval"].Metrics["hybrid"]["recall@5"]);
        // Sorted keys, so a diff shows the number that moved and nothing else.
        Assert.True(text.IndexOf("retrieval", StringComparison.Ordinal) < text.IndexOf("selection", StringComparison.Ordinal));
        Assert.True(text.IndexOf("\"mrr\"", StringComparison.Ordinal) < text.IndexOf("\"recall@5\"", StringComparison.Ordinal));
    }
}
