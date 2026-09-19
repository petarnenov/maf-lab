# Billing Setup During Onboarding

## Required Information

Before a new Acme Wealth Partners household can be billed, OPS needs the signed advisory agreement, the fee disclosure signed by the client, the list of accounts with custodian numbers, and the household grouping confirmed by the advisor. Missing any of these items delays setup and increases the risk of an FS-REQUIRED failure at quarter end.

## Configuration Steps

### Household Record

OPS creates the household record, links every account, and sets the billing frequency to quarterly in arrears. Institutional exceptions require FIRM_ADMIN approval before a different frequency is selected.

### Schedule Assignment

OPS assigns ACME-TIER-2026 to every account unless the fee exceptions register contains an approved exception for the household. The assignment effective date matches the funding date of the first account.

## Verification

After configuration, OPS runs a billing preview for the current quarter. The preview must show every account with a schedule, a current valuation, and a prorated fee consistent with the funding date. The advisor confirms the preview before the household is marked as active.
