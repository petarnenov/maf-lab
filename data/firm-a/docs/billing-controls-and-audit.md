# Billing Controls and Audit

## Control Objectives

The billing control framework at Acme Wealth Partners is designed to ensure that every client is billed on the schedule they agreed to, using accurate asset values, and that large or unusual charges receive independent review. The framework is tested annually by the internal audit team.

## Key Controls

### Preventive Controls

The platform blocks runs with missing schedules (FS-REQUIRED) or stale valuations (AUM-STALE). Invoices over $25,000 are held for FIRM_ADMIN approval, and fee schedule edits are limited to FIRM_ADMIN users.

### Detective Controls

The billing desk reviews the approval log weekly for self-approvals, reviews the fee exceptions register quarterly, and reconciles custodian deductions after each run. Any CUSTODIAN-MISMATCH exception is logged with its resolution.

## Evidence Retention

Run reports, approval logs, reconciliation workpapers and credit approvals are retained for seven years. Auditors with the READ_ONLY role can retrieve run history and invoice images directly from the platform, while workpapers are stored in the operations document archive.
