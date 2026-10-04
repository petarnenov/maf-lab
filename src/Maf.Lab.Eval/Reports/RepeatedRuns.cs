using Maf.Lab.Domain.Evals;
using Maf.Lab.Eval.Suites;

namespace Maf.Lab.Eval.Reports;

/// <summary>
/// A suite run several times and read as one (adopt-meai-evaluation, DECISIONS.md §78): a suite that drives the live
/// agent answers differently on every run, and one run in the tail of that spread reads as a regression — or, accepted,
/// makes every run after it read as one. The mean of a few runs is what the gate compares and what the baseline holds.
/// </summary>
public static class RepeatedRuns
{
    /// <summary>
    /// Per variant, each metric's mean over the runs that report it; the variant's thresholds re-applied to the means;
    /// every run's failures kept, each marked with its run (<c>[r2] …</c>). A single run is returned unchanged.
    /// </summary>
    public static IReadOnlyList<EvalVariantResult> Mean(IReadOnlyList<IReadOnlyList<EvalVariantResult>> runs)
    {
        if (runs.Count == 1)
        {
            return runs[0];
        }
        return
        [
            .. runs[0].Select(first =>
            {
                var same = runs.Select(r => r.FirstOrDefault(v => v.Name == first.Name)).OfType<EvalVariantResult>().ToList();
                var metrics = same.SelectMany(v => v.Metrics.Keys).Distinct(StringComparer.Ordinal)
                    .ToDictionary(k => k, k => same.Where(v => v.Metrics.ContainsKey(k)).Average(v => v.Metrics[k]));
                var failures = runs.SelectMany((r, i) => r.Where(v => v.Name == first.Name)
                    .SelectMany(v => v.Failures.Select(f => new EvalCaseFailure(f.CaseId, $"[r{i + 1}] {f.Reason}")))).ToList();
                return SuiteContext.Variant(first.Name, metrics, first.Thresholds, first.Cases, failures);
            }),
        ];
    }
}
