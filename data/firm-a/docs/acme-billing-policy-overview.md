# Acme Wealth Partners Billing Policy Overview

## Purpose and Scope

This document describes how Acme Wealth Partners bills advisory clients on the shared TAMP billing platform. It applies to every household serviced by Acme advisors, including legacy accounts migrated from the 2023 custodian conversion. Platform-wide behaviour, such as run statuses and failure codes, is documented in the shared platform guides; this policy only records where Acme Wealth Partners deviates from or extends those defaults. Internal reference code: ACME-CANARY-4410. Questions about this policy should be routed to the Acme billing desk rather than to the platform support queue, because the desk owns firm-specific configuration and approvals.

## Key Firm-Specific Rules

### Fee Schedule Standard

All new households onboarded after January 1, 2026 are assigned the custom tiered schedule ACME-TIER-2026 unless the advisor documents an approved exception. Legacy households remain on their existing schedule until their next annual review, at which point the advisor must either migrate them to ACME-TIER-2026 or record the reason for keeping the legacy schedule in the household notes.

### Invoice Approval Threshold

Any invoice whose total fee for a single billing period exceeds $25,000 must be approved by a user holding the FIRM_ADMIN role before it can be released to the client or deducted at the custodian. Invoices at or below $25,000 are released automatically once the billing run completes successfully.

## Billing Frequency

Acme Wealth Partners bills quarterly in arrears based on the average daily AUM for the quarter. Monthly billing is available only for institutional relationships approved by the FIRM_ADMIN group, and those relationships must still use a tiered schedule rather than a flat rate.
