using Maf.Lab.Domain.Billing;
using Maf.Lab.Domain.Tenancy;

namespace Maf.Lab.Retrieval.Billing;

/// <summary>
/// An account's fee as it stands: what the seed says plus every adjustment that has been applied to it.
/// The seed is a fixture and stays read-only; the ledger is the only thing that moves.
/// </summary>
public sealed class AccountFees(BillingAccountStore accounts, FeeAdjustmentLedger ledger)
{
    /// <summary>The account with its current fee, or null when the caller's firm has no such account.</summary>
    public BillingAccount? Current(Principal principal, string accountId)
    {
        var account = accounts.Find(principal, accountId);
        return account is null
            ? null
            : account with { Fee = account.Fee + ledger.AppliedTotal(principal, account.AccountId) };
    }

    /// <summary>The seeded fee, which is what the ledger adjusts from.</summary>
    public decimal? SeededFee(Principal principal, string accountId) => accounts.Find(principal, accountId)?.Fee;
}
