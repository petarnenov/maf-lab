// Billing run lifecycle: pending -> running -> completed | failed; failed -> pending on re-run.
using System;
using System.Collections.Generic;

namespace Tamp.Billing.Runs;

public enum RunStatus { Pending, Running, Completed, Failed }

public sealed class BillingRun
{
    public required string RunId { get; init; }
    public required string FirmId { get; init; }
    public required DateOnly PeriodStart { get; init; }
    public required DateOnly PeriodEnd { get; init; }
    public RunStatus Status { get; private set; } = RunStatus.Pending;
    public string? FailureReason { get; private set; }
    public int Attempt { get; private set; } = 1;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    private static readonly Dictionary<RunStatus, RunStatus[]> Allowed = new()
    {
        [RunStatus.Pending] = [RunStatus.Running],
        [RunStatus.Running] = [RunStatus.Completed, RunStatus.Failed],
        [RunStatus.Failed] = [RunStatus.Pending],
        [RunStatus.Completed] = [],
    };

    /// <summary>Moves the run to a new status, rejecting transitions the lifecycle does not allow.</summary>
    public void TransitionTo(RunStatus next, string? failureReason = null)
    {
        if (Array.IndexOf(Allowed[Status], next) < 0)
            throw new InvalidOperationException($"Run {RunId}: {Status} -> {next} is not allowed.");
        if (next == RunStatus.Failed && string.IsNullOrWhiteSpace(failureReason))
            throw new ArgumentException("A failed run must carry a failure reason.", nameof(failureReason));

        Status = next;
        FailureReason = next == RunStatus.Failed ? failureReason : null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Requests a re-run of a failed run: a new attempt under the same run id.</summary>
    public void RequestRerun()
    {
        TransitionTo(RunStatus.Pending);
        Attempt++;
    }
}

public static class FailureCodes
{
    public const string FeeScheduleRequired = "FS-REQUIRED";
    public const string AumStale = "AUM-STALE";
    public const string CustodianMismatch = "CUSTODIAN-MISMATCH";
    public const string ProrationGap = "PRORATION-GAP";

    /// <summary>Builds the canonical failure reason, e.g. "FS-REQUIRED: fee schedule missing for 3 accounts".</summary>
    public static string Reason(string code, string detail) => $"{code}: {detail}";

    /// <summary>Extracts the code (text before the first colon) from a failure reason.</summary>
    public static string? CodeOf(string? reason) =>
        reason is null ? null : reason.Split(':', 2)[0].Trim();
}
