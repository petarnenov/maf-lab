# Adjustments and Credits

## Why Completed Runs Are Not Edited

Once a billing run is completed, its results are frozen so that historical invoices remain reproducible and auditable. Corrections are made through adjustments and credits, which are posted as separate records and appear on the next invoice or on a corrective invoice. This design keeps a clean audit trail: every change to what a client pays can be traced to a user, a reason, and a date. It also means that reports for earlier periods never change silently after they have been shared with clients, finance teams, or regulators.

## Types of Corrections

### Adjustments and Billing Credits

**Adjustments.** An adjustment increases or decreases the fee for an account in a specific period. Typical reasons include a late-arriving valuation correction, a schedule that should have been applied from an earlier effective date, or a negotiated fee waiver. Adjustments reference the original run id and period so reporting can show the corrected fee for that period.

**Billing Credits.** A billing credit reduces what a client owes, usually because of an overcharge, a service issue, or an advance-billing refund for a closed account. Credits can be applied to the next invoice automatically or refunded to the account through the custodian. Credits above the firm's configured limit require FIRM_ADMIN approval.

## Controls

### Approval and Limits and Reporting

**Approval and Limits.** OPS users can create adjustments and credits, but they remain in a draft state until approved when they exceed the firm's limit. ADVISOR users can request a credit for their clients but cannot approve it. Every correction requires a reason code and a free-text note, and the note is included in the audit log but not on the client invoice.

**Reporting.** The adjustments report lists all corrections by period, account, reason code, and approver. Firms should review it at period close to confirm that recurring corrections are not masking a systemic problem, such as a schedule that was set up with the wrong tiers.
