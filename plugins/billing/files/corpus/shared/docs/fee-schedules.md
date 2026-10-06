# Fee Schedules

## Overview

A fee schedule is the rule set that turns an account's assets under management into an advisory fee. Every billable account must resolve to exactly one active fee schedule for a billing period. If no schedule can be resolved, the billing run fails with the failure code FS-REQUIRED and the affected accounts are listed in the run diagnostics. Fee schedules belong to a firm and are managed by users with the FIRM_ADMIN role. Schedules are identified by a short schedule code that is unique within the firm.

## Schedule Types

### Flat Schedules, Tiered Schedules and Breakpoint Schedules

**Flat Schedules.** A flat schedule charges a single annual rate on the full billable AUM, for example 1.00% per year. Some firms also use flat dollar schedules that charge a fixed amount per period regardless of AUM, which is common for financial-planning retainers. Flat schedules are the simplest to explain to clients and are often used for small accounts or legacy relationships.

**Tiered Schedules.** A tiered schedule splits AUM into bands and applies a different rate to each band. For example, the first $1,000,000 is charged at 1.00%, the next $4,000,000 at 0.75%, and everything above $5,000,000 at 0.50%. The total fee is the sum of the band fees, so a client's effective rate declines smoothly as assets grow.

**Breakpoint Schedules.** A breakpoint schedule applies a single rate to the entire AUM, chosen by the band the total AUM falls into. Once a household crosses a breakpoint, the lower rate applies to all of its assets, not only to the portion above the threshold. Breakpoint schedules produce a step change in fees when a threshold is crossed, which clients sometimes notice when balances hover around a breakpoint.

## Assignment and Resolution

### Resolution Order and Minimums and Caps

**Resolution Order.** The billing engine resolves a schedule in this order: an account-level assignment, then a household-level assignment, then the firm default schedule if one is configured. The first match wins. Firms that do not configure a default schedule must assign schedules explicitly to every new account before its first billing period.

**Minimums and Caps.** Any schedule may define an annual minimum fee and an annual maximum fee. Minimums are prorated like the fee itself. When a minimum applies, the invoice line shows both the calculated fee and the minimum adjustment so the client can see why the amount differs.
