# Handling FS-REQUIRED Failures at Acme Wealth Partners

## What the Failure Means

A billing run fails with FS-REQUIRED when at least one account in scope has no fee schedule assigned for the billing period. At Acme Wealth Partners this almost always happens for one of three reasons: a newly opened account was added to a household without a schedule, a household split created a new household without a schedule, or a legacy schedule was retired before the household was migrated to ACME-TIER-2026.

## Firm-Specific Expectations

### Notification

In addition to the platform procedure, Acme requires that the billing desk be notified of every FS-REQUIRED failure, even when OPS can fix it immediately. The desk uses these notifications to spot onboarding gaps and to coach advisors on schedule assignment.

### Default Schedule

When no exception is documented, the correct schedule to assign is ACME-TIER-2026. OPS must not invent ad hoc flat fees to get a run through, because that bypasses the fee exceptions register.

## After the Fix

Once schedules are assigned, OPS validates with a billing preview and re-runs the failed billing run. If the re-run produces any invoice above $25,000, it enters the FIRM_ADMIN approval queue as usual.
