namespace Maf.Lab.Domain.Billing;

/// <summary>
/// The write tool's name, shared by the server that offers it and the flow that confirms it. The keys its question and
/// its confirmed call carry are every write tool's: <see cref="Maf.Lab.Domain.Writes.WriteConfirmationKeys"/>.
/// </summary>
public static class FeeAdjustmentTool
{
    public const string Name = "propose_fee_adjustment";
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

/// <summary>
/// A proposal a conversation is still waiting on. Everything a person needs to answer it, and nothing they
/// would have to answer it with — the state that actually executes never leaves the run that issued it.
/// </summary>
public sealed record PendingProposal(
    string AdjustmentId,
    FeeAdjustmentSummary Adjustment,
    string Question,
    DateTimeOffset? ExpiresAt);

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
