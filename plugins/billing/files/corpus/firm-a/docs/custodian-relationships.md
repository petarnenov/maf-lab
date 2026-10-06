# Custodian Relationships for Acme Wealth Partners

## Custodians in Use

Acme Wealth Partners custodies client assets with two primary custodians and one legacy custodian that holds a small number of trust accounts. Each custodian receives a quarterly fee deduction file generated from the completed billing run. The file format differs by custodian, and the platform selects the correct template based on the custodian code stored on each account. Internal reference code: ACME-CANARY-4410. New custodian relationships must be approved by a FIRM_ADMIN and configured by the platform team before any accounts are billed through them.

## Fee Deduction Timing

### Primary Custodians

Fee files for the primary custodians are submitted on the fifth business day after quarter end, provided that all invoices over $25,000 have been approved. Invoices still awaiting FIRM_ADMIN approval are held back and submitted in a supplemental file once approved.

### Legacy Custodian

The legacy custodian accepts only manual fee instructions. OPS prepares a signed instruction letter for each trust account, and the billing desk keeps a copy with the run id in the billing log.

## Reconciliation Expectations

After custodians process the deductions, OPS reconciles the amounts actually debited against the invoiced amounts. Any difference greater than $5 is treated as a CUSTODIAN-MISMATCH exception and must be resolved before the billing period can be closed.
