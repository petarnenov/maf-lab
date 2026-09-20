namespace Maf.Lab.Domain.Evals;

/// <summary>How a metric compares with the value this repository has accepted.</summary>
public enum MetricStatus
{
    /// <summary>Below the baseline by more than the tolerance: this fails the run.</summary>
    Regression,
    /// <summary>Below the baseline, but within the tolerance a live model's variation needs.</summary>
    Noise,
    /// <summary>At or above the baseline.</summary>
    Improvement,
    /// <summary>The baseline does not mention it — reported, never silently ignored.</summary>
    New,
    /// <summary>The baseline mentions it but the run did not produce it (a rename, usually).</summary>
    Missing,
}

/// <param name="Delta">Value minus baseline; negative is a drop. Null when there is nothing to compare.</param>
public sealed record MetricComparison(
    string Variant,
    string Metric,
    double? Baseline,
    double? Value,
    double? Delta,
    MetricStatus Status);

/// <param name="Metrics">variant → metric → accepted value.</param>
/// <param name="AcceptedFrom">The run these numbers came from: a baseline states what was produced, not what was typed.</param>
public sealed record SuiteBaseline(
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> Metrics,
    string AcceptedFrom,
    DateTimeOffset AcceptedAt);

/// <summary>What "good" currently is, per suite — committed, so a change to it is reviewable.</summary>
public sealed record EvalBaseline(IReadOnlyDictionary<string, SuiteBaseline> Suites)
{
    public static EvalBaseline Empty { get; } = new(new Dictionary<string, SuiteBaseline>());
}
