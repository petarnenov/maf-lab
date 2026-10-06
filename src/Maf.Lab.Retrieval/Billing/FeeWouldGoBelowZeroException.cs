namespace Maf.Lab.Retrieval.Billing;

// names a domain until the extract-evals-plugin follow-up moves it (introduce-plugins 8.1)
// The billing host, which stays here until the eval stops hosting it in-process.
/// <summary>
/// A reduction the ledger refused because it would leave the fee below zero. Nothing was written.
/// Raised inside the apply transaction, where the current fee is known without racing another replica.
/// </summary>
internal sealed class FeeWouldGoBelowZeroException(string accountId, decimal previousFee, decimal resultingFee, string currency)
    : InvalidOperationException($"The fee on {accountId} would go below zero.")
{
    public string AccountId { get; } = accountId;

    public decimal PreviousFee { get; } = previousFee;

    public decimal ResultingFee { get; } = resultingFee;

    public string Currency { get; } = currency;
}
