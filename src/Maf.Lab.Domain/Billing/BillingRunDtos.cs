namespace Maf.Lab.Domain.Billing;

/// <summary>Result of get_billing_run_status. Deliberately has no free-text note field.</summary>
public sealed record BillingRunStatus(
    string RunId,
    string Status,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    int AccountCount,
    string? FailureReason,
    DateTimeOffset UpdatedAt);

public sealed record BillingRunSummary(
    string RunId,
    string Status,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    int AccountCount);

public sealed record SearchBillingRunsResult(IReadOnlyList<BillingRunSummary> Runs, int TotalMatches, bool Truncated);
