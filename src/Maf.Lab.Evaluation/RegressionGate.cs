using System.Globalization;
using Maf.Lab.Domain.Evals;

namespace Maf.Lab.Eval.Reports;

/// <summary>
/// Compares a run with the accepted baseline. Thresholds answer "is this usable at all"; this answers "is this
/// worse than it was" — the space a suite can slide through while still passing its floors.
/// </summary>
public static class RegressionGate
{
    /// <summary>One tolerance for every metric. For a caller that genuinely has one; production resolves per metric.</summary>
    public static IReadOnlyList<MetricComparison> Compare(SuiteBaseline? baseline, IReadOnlyList<EvalVariantResult> variants, double tolerance) =>
        Compare(baseline, variants, _ => tolerance);

    /// <summary>
    /// <paramref name="toleranceFor"/> is asked per metric name, because noise is a property of how a metric is
    /// produced. A metric that does not move must not inherit the width another metric needed.
    /// </summary>
    public static IReadOnlyList<MetricComparison> Compare(SuiteBaseline? baseline, IReadOnlyList<EvalVariantResult> variants,
        Func<string, double> toleranceFor)
    {
        var comparisons = new List<MetricComparison>();
        foreach (var variant in variants)
        {
            var acceptedMetrics = baseline is not null && baseline.Metrics.TryGetValue(variant.Name, out var m) ? m : null;
            foreach (var (metric, value) in variant.Metrics.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                if (acceptedMetrics is null || !acceptedMetrics.TryGetValue(metric, out var previous))
                {
                    // Reported, not ignored: a renamed or added metric must not quietly escape the gate.
                    comparisons.Add(new MetricComparison(variant.Name, metric, null, value, null, MetricStatus.New));
                    continue;
                }
                var delta = value - previous;
                var tolerance = toleranceFor(metric);
                // A drop *exactly* at the tolerance must pass, and binary floating point does not agree that
                // 1.0 - 0.98 is 0.02, so the comparison carries a hair of slack.
                var status = delta >= 0 ? MetricStatus.Improvement
                    : -delta <= tolerance + 1e-9 ? MetricStatus.Noise
                    : MetricStatus.Regression;
                comparisons.Add(new MetricComparison(variant.Name, metric, previous, value, delta, status));
            }

            // The other half of a rename: the baseline knows a metric this run did not produce.
            foreach (var (metric, previous) in (acceptedMetrics ?? new Dictionary<string, double>()).OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                if (!variant.Metrics.ContainsKey(metric))
                {
                    comparisons.Add(new MetricComparison(variant.Name, metric, previous, null, null, MetricStatus.Missing));
                }
            }
        }
        return comparisons;
    }

    public static bool HasRegression(IEnumerable<MetricComparison> comparisons) =>
        comparisons.Any(c => c.Status == MetricStatus.Regression);

    /// <summary>One line per metric that moved, naming both values and the size of the move.</summary>
    public static IEnumerable<string> Describe(IEnumerable<MetricComparison> comparisons, string suite)
    {
        foreach (var c in comparisons)
        {
            var line = c.Status switch
            {
                MetricStatus.Regression => $"✗ REGRESSION {suite}/{c.Variant} {c.Metric}: {Num(c.Baseline)} → {Num(c.Value)} ({Signed(c.Delta)})",
                MetricStatus.Improvement when c.Delta > 0 => $"↑ improved {suite}/{c.Variant} {c.Metric}: {Num(c.Baseline)} → {Num(c.Value)} ({Signed(c.Delta)})",
                MetricStatus.Noise => $"· within tolerance {suite}/{c.Variant} {c.Metric}: {Num(c.Baseline)} → {Num(c.Value)} ({Signed(c.Delta)})",
                MetricStatus.New => $"+ new metric {suite}/{c.Variant} {c.Metric}: {Num(c.Value)} (no baseline)",
                MetricStatus.Missing => $"? missing {suite}/{c.Variant} {c.Metric}: baseline {Num(c.Baseline)}, not produced by this run",
                _ => "",
            };
            if (line.Length > 0)
            {
                yield return line;
            }
        }
    }

    private static string Num(double? value) => value?.ToString("0.###", CultureInfo.InvariantCulture) ?? "—";

    private static string Signed(double? delta) =>
        delta is null ? "—" : (delta >= 0 ? "+" : "") + delta.Value.ToString("0.###", CultureInfo.InvariantCulture);
}
