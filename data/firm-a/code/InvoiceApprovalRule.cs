// Acme Wealth Partners: invoices over $25,000 require FIRM_ADMIN approval.
using System;

namespace Acme.Billing;

public enum ApprovalState { NotRequired, AwaitingApproval, Approved, Rejected }

public sealed record Invoice(string InvoiceId, string HouseholdId, string AdvisorUserId, decimal GrossTotal, decimal Credits);

public static class InvoiceApprovalRule
{
    public const decimal Threshold = 25_000m;

    /// <summary>The threshold is evaluated on the gross total, before credits are applied.</summary>
    public static bool RequiresFirmAdminApproval(Invoice invoice) => invoice.GrossTotal > Threshold;

    public static ApprovalState InitialState(Invoice invoice) =>
        RequiresFirmAdminApproval(invoice) ? ApprovalState.AwaitingApproval : ApprovalState.NotRequired;

    /// <summary>Only FIRM_ADMIN may approve, and not for their own households (segregation of duties).</summary>
    public static bool CanApprove(Invoice invoice, string userId, string role)
    {
        if (!string.Equals(role, "FIRM_ADMIN", StringComparison.Ordinal)) return false;
        return !string.Equals(invoice.AdvisorUserId, userId, StringComparison.Ordinal);
    }

    /// <summary>Invoices may be included in custodian fee files only once released.</summary>
    public static bool IsReleasable(ApprovalState state) =>
        state is ApprovalState.NotRequired or ApprovalState.Approved;
}
