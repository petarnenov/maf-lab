namespace Maf.Lab.Domain.Evals;

/// <summary>JSON report written by Maf.Lab.Eval and read by the web /evals page.</summary>
public sealed record EvalReport(
    string RunId,
    string Suite,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    IReadOnlyDictionary<string, string> Settings,
    IReadOnlyList<EvalVariantResult> Variants,
    bool Passed);

/// <summary>One configuration of a suite, e.g. retrieval with mode=hybrid, rerank=off.</summary>
public sealed record EvalVariantResult(
    string Name,
    IReadOnlyDictionary<string, double> Metrics,
    IReadOnlyDictionary<string, double> Thresholds,
    bool Passed,
    int Cases,
    IReadOnlyList<EvalCaseFailure> Failures);

public sealed record EvalCaseFailure(string CaseId, string Reason);

public sealed record EvalReportSummary(string RunId, string Suite, DateTimeOffset StartedAt, bool Passed, IReadOnlyList<EvalVariantResult> Variants);
