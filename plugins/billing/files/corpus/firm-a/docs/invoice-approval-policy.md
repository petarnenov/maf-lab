# Invoice Approval Policy at Acme Wealth Partners

## Approval Threshold

Acme Wealth Partners requires FIRM_ADMIN approval for every invoice over $25,000 in a single billing period. The threshold is evaluated per invoice, not per account, so a household invoice that aggregates several accounts is measured on its combined total. Invoices that are exactly $25,000 do not require approval. The rule exists because large invoices carry higher reputational risk if they contain valuation or schedule errors, and a second review catches most of those mistakes before money leaves a client account. Internal reference code: ACME-CANARY-4410.

## Who May Approve

### Eligible Roles

Only users with the FIRM_ADMIN role can approve invoices above the threshold. Users with the ADVISOR role can view the pending approval and add comments, but cannot approve their own clients' invoices. OPS users can prepare supporting documentation, such as custodian statements, and READ_ONLY users can see the approval queue but take no action.

### Segregation of Duties

A FIRM_ADMIN who is also the advisor of record for the household must ask a second FIRM_ADMIN to approve. The platform does not enforce this automatically, so the billing desk reviews the approval log weekly and flags any self-approvals for remediation.

## Approval Workflow

When a billing run completes, invoices above $25,000 are placed in the Awaiting Approval queue and are excluded from custodian fee files. The approver reviews the AUM valuation, the fee schedule applied, and any adjustments or credits. Approved invoices are released to the next custodian file; rejected invoices are returned to OPS with a comment explaining the required correction.
