namespace Maf.Lab.Domain.Evals;

/// <summary>JSON report written by Maf.Lab.Eval and read by the web /evals page.</summary>
/// <param name="Passed">Every threshold met and nothing regressed against the baseline.</param>
/// <param name="Comparisons">What moved against the baseline; empty in reports written before the gate existed.</param>
public sealed record EvalReport(
    string RunId,
    string Suite,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    IReadOnlyDictionary<string, string> Settings,
    IReadOnlyList<EvalVariantResult> Variants,
    bool Passed,
    IReadOnlyList<MetricComparison>? Comparisons = null);

/// <summary>One configuration of a suite, e.g. retrieval with mode=hybrid, rerank=off.</summary>
public sealed record EvalVariantResult(
    string Name,
    IReadOnlyDictionary<string, double> Metrics,
    IReadOnlyDictionary<string, double> Thresholds,
    bool Passed,
    int Cases,
    IReadOnlyList<EvalCaseFailure> Failures);

public sealed record EvalCaseFailure(string CaseId, string Reason);

public sealed record EvalReportSummary(string RunId, string Suite, DateTimeOffset StartedAt, bool Passed,
    IReadOnlyList<EvalVariantResult> Variants, IReadOnlyList<MetricComparison>? Comparisons = null);
