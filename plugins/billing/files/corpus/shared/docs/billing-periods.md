# Billing Periods and Methods

## Period Configuration

Each firm configures a billing frequency, either monthly or quarterly, and a billing method, either arrears or advance. The combination determines the default period start and end dates and the valuation date for each run. Periods are always aligned to calendar months or quarters; custom periods are not supported because they complicate custodian fee files and client reporting. A firm can change its configuration only at a period boundary, and the change must be made by a FIRM_ADMIN. The configuration is shown on every invoice so clients understand which months a fee covers.

## Period States

### Open and Closed

**Open.** An open period accepts billing runs, re-runs, adjustments, and credits. Multiple attempts may be made for the same period until a run completes successfully.

**Closed.** A closed period is locked for new runs. Adjustments that relate to a closed period are posted to the next open period with a reference to the original one. Closing a period is a deliberate action performed after all invoices are approved and custodian fee deductions are reconciled. Firms typically close a period within a few weeks of its end, once the custodian confirmation files have arrived.

## Arrears Versus Advance

### Arrears Billing and Advance Billing

**Arrears Billing.** In arrears billing the fee is calculated after the period ends, based on AUM at the end of the period or its average. Accounts that close during the period are billed for the days they were open, and there is no need for refunds.

**Advance Billing.** In advance billing the fee is calculated at the start of the period based on the opening AUM. Accounts that open during the period are billed a prorated fee on the next run, and accounts that close early receive a prorated credit for unused days. Advance billing gives firms predictable revenue but requires more corrections when accounts change mid-period.
