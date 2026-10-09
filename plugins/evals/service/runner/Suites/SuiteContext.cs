using Maf.Lab.Domain.Evals;

namespace Maf.Lab.Eval.Suites;

/// <param name="CorpusLanguage">Language the indexed documents are written in; a case that declares none counts as this.</param>
public sealed record SuiteContext(string DatasetRoot, EvalOptions Options, int? Limit, Action<string> Progress, string CorpusLanguage = "en")
{
    public IReadOnlyDictionary<string, double> ThresholdsFor(string suite) =>
        Options.Thresholds.TryGetValue(suite, out var t) ? t : new Dictionary<string, double>();

    public IEnumerable<T> Take<T>(IEnumerable<T> rows) => Limit is { } n ? rows.Take(n) : rows;

    /// <summary>A variant passes when every configured threshold is met.</summary>
    public static EvalVariantResult Variant(string name, Dictionary<string, double> metrics, IReadOnlyDictionary<string, double> thresholds, int cases, List<EvalCaseFailure> failures)
    {
        var rounded = Metrics.Round(metrics);
        var passed = thresholds.All(t => rounded.TryGetValue(t.Key, out var v) && v >= t.Value);
        return new EvalVariantResult(name, rounded, thresholds, passed, cases, failures);
    }
}
