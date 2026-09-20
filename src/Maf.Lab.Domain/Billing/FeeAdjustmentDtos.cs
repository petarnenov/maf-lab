namespace Maf.Lab.Domain.Billing;

/// <summary>
/// The write tool's contract, shared by the server that offers it and the host that consumes it: its name,
/// the input it asks for, and the _meta keys the question carries.
/// </summary>
public static class FeeAdjustmentTool
{
    public const string Name = "propose_fee_adjustment";

    /// <summary>The key of the one input the tool asks for.</summary>
    public const string ConfirmationKey = "confirmation";

    /// <summary>The summary a person checks, carried in the question's _meta.</summary>
    public const string SummaryKey = "maf-lab/adjustment";

    /// <summary>The opaque state, repeated in _meta because a client resolving an elicitation cannot see it otherwise.</summary>
    public const string StateKey = "maf-lab/proposal-state";

    /// <summary>When the proposal stops being answerable. Only the signer knows it; the host must be told.</summary>
    public const string ExpiresAtKey = "maf-lab/proposal-expires-at";
}

/// <summary>An account as anything outside the store may see it. Deliberately has no free-text note field.</summary>
public sealed record BillingAccount(
    string AccountId,
    string Name,
    decimal Fee,
    string Currency,
    DateOnly NextPeriodStart,
    DateOnly NextPeriodEnd);

/// <summary>
/// What a person is asked to confirm: the account, what it costs now, the change, and what it would cost.
/// Contains nothing free-text — the reason the advisor gave is not part of it.
/// </summary>
public sealed record FeeAdjustmentSummary(
    string AdjustmentId,
    string AccountId,
    string AccountName,
    decimal CurrentFee,
    decimal Amount,
    decimal ResultingFee,
    string Currency,
    DateOnly PeriodStart,
    DateOnly PeriodEnd);

/// <summary>The outcome of applying an adjustment. <paramref name="AlreadyApplied"/> marks a repeated confirmation.</summary>
public sealed record FeeAdjustmentApplied(
    string AdjustmentId,
    string AccountId,
    decimal PreviousFee,
    decimal Amount,
    decimal CurrentFee,
    string Currency,
    DateTimeOffset AppliedAt,
    bool AlreadyApplied);
