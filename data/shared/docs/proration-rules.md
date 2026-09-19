# Proration Rules

## When Proration Applies

Proration adjusts a fee when an account is billable for only part of a billing period. The most common cases are new accounts funded mid-period, accounts closed or transferred out mid-period, and changes to the assigned fee schedule that take effect inside a period. Firms can additionally enable flow-based proration, which adjusts fees for contributions and withdrawals above a configured threshold, typically $10,000 or 10% of the account value. Proration is shown on the invoice line as the number of billable days in the period.

## Day-Count Conventions

### Supported Conventions and Billable Days

**Supported Conventions.** The platform supports actual/365, actual/360, and 30/360 day counts. Actual/365 is the default and divides the number of billable days by 365 even in leap years. Actual/360 slightly increases the effective fee and is used by some legacy agreements. The 30/360 convention treats every month as 30 days, which makes monthly fees identical across months and is popular with firms that bill monthly in advance.

**Billable Days.** Billable days are counted inclusively from the later of the period start and the account funding date to the earlier of the period end and the account closing date. An account funded on the last day of the period is billable for one day.

## Edge Cases

### Schedule Changes Mid-Period and Advance Billing Refunds

**Schedule Changes Mid-Period.** When a schedule assignment changes inside a period, the engine splits the period into segments and computes each segment with the schedule version in force. Segments must be contiguous and non-overlapping. Any gap or overlap causes the run to fail with PRORATION-GAP, because the engine cannot determine the intended billable days without guessing.

**Advance Billing Refunds.** For firms billing in advance, a closing account generates a prorated refund for the unused days of the period. The refund is created as a billing credit on the next run rather than by editing the completed run, keeping historical invoices unchanged.
