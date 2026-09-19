# Billing Adjustments Policy

## Types of Adjustments

Contoso Advisors uses adjustments to correct or supplement the regular quarterly installment. Three adjustment types are used. Debit adjustments add a charge, such as the household rebalancing fee or catch-up proration for a late-linked account. Credit adjustments return money, such as termination refunds or corrections for over-billing. Zero adjustments record a waived fee for audit purposes. Adjustments are always attached to a household and a billing period, and they flow into the next billing run for that period.

## Approval Rules

### Who Approves

The OPS role can enter any adjustment. Credits above a small threshold and all waivers require approval from a FIRM_ADMIN, normally the operations lead. Debits for the household rebalancing fee require confirmation that the client request is documented.

### Documentation

Each adjustment carries a reason code and a free-text note. The note should reference the client request, the email, or the termination letter. Adjustments without a note are rejected during invoice review.

## Timing

Adjustments must be entered before the quarterly run starts to appear on that quarter's invoice. Adjustments entered after a run has completed are held for the next run. Contoso Advisors never edits a completed run to add an adjustment. If a correction is urgent, the operations lead can issue a stand-alone credit, which the platform processes in a small off-cycle run.
