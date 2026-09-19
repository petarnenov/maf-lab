# Fee Exceptions Register

## Purpose

The fee exceptions register records every Acme Wealth Partners household that is not billed on the standard ACME-TIER-2026 schedule. Exceptions include legacy schedules awaiting migration, negotiated flat fees for family office relationships, and pro bono accounts for employee families. The register lets compliance confirm that each exception was approved and is still justified.

## What Must Be Recorded

### Required Fields

Each entry lists the household name, the schedule code in use, the approving FIRM_ADMIN, the approval date, and the review date. Entries without a review date are treated as expired and are reported to the billing desk at the start of each quarter.

### Expiry Handling

When an exception expires, the advisor has thirty days to either renew it with a new approval or migrate the household to ACME-TIER-2026. If neither happens, OPS migrates the household at the next period start and notifies the advisor.

## Relationship to Billing Runs

The billing platform does not read the register directly. A household with an expired exception still bills on its assigned schedule, so the register review is the only control that catches stale exceptions. Missing schedule assignments, by contrast, are caught automatically by the FS-REQUIRED check.
