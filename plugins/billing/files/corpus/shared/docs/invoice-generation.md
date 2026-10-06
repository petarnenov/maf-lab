# Invoice Generation

## From Completed Run to Invoice

Invoices are generated only from completed billing runs. For each billing unit, which is a household or a standalone account, the platform creates one invoice document that lists every account, its billable AUM, the schedule used, the period covered, any proration, and the resulting fee. Adjustments and credits posted against the period appear as separate lines below the calculated fees. The invoice total is the sum of all lines and is never negative; if credits exceed fees, the remainder is carried forward to the next period.

## Invoice Numbering and Format

### Numbering and Content and Formatting

**Numbering.** Invoice numbers are sequential within a firm and include the period end year, for example INV-2026-000123. Numbers are assigned at generation time and are never reused, even if an invoice is later voided. Voided invoices remain visible with a void reason so auditors can trace the full sequence.

**Content and Formatting.** Amounts are shown in the firm's reporting currency with two decimals and thousands separators. Rates are shown as annual percentages with up to four decimals. Each invoice includes the valuation date, the billing method (arrears or advance), and the schedule code. Firms can upload a logo and a footer disclosure, but the calculation sections have a fixed layout so that invoices remain consistent across the platform.

## Approval and Delivery

### Approval and Delivery

**Approval.** By default, invoices move directly from draft to approved when the run completes. Firms can configure an approval threshold so that invoices above a certain amount require a FIRM_ADMIN to approve them before delivery. Firm-specific thresholds are documented in each firm's own policies.

**Delivery.** Approved invoices can be delivered to the client portal, exported as PDF, or included in a custodian fee file for direct debit. Delivery events are logged with the user and timestamp. Invoices that are awaiting approval are listed on the firm dashboard with their amount and age.
