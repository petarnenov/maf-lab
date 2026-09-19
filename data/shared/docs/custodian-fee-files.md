# Custodian Fee Files

## Direct Billing Overview

Most firms collect advisory fees by having the custodian debit them directly from client accounts. After invoices are approved, the platform produces a custodian fee file per custodian that lists each account number, the fee amount, and the period covered. The custodian processes the file, debits the accounts, and later returns a confirmation file that lists the amounts actually deducted. Firms that do not use direct billing can still export fee files for their own records, but those files are not sent to a custodian and do not require reconciliation before period close.

## File Generation

### Content Checks and Release

**Content Checks.** Before a file is released, the platform checks that every account is open at the custodian, that account numbers match the custodian master, and that the fee does not exceed the available cash when the custodian requires it. Problems detected during the run cause a CUSTODIAN-MISMATCH failure; problems detected later, at file generation, block only the affected lines.

**Release.** Fee files are released by OPS users. Each release is logged, and a file can only be released once per custodian per period. If a correction is needed, a supplemental file containing only the corrected lines is produced.

## Reconciliation

### Matching Deductions

When the confirmation file arrives, the platform matches each deducted amount to the invoice line it relates to. Differences are flagged as reconciliation exceptions. Common causes are insufficient cash, accounts transferred before processing, and custodian rounding. Exceptions must be resolved, either by re-submitting the fee, billing the client directly, or writing off the difference with a credit, before the billing period can be closed. The reconciliation status is shown per custodian so that OPS can see which confirmations are still outstanding.
