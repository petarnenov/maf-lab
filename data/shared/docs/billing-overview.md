# Billing Overview

## Purpose of the Billing Module

The billing module of the turnkey asset management platform calculates advisory fees for every account that a firm manages on the platform. Fees are derived from assets under management (AUM) at a valuation date, the fee schedule assigned to the account or its household, and the billing period configured for the firm. The module produces invoices, posts fee debits to custodians when direct billing is enabled, and records an auditable history of every calculation. All billing data is isolated per firm: a firm can only see its own accounts, schedules, runs, and invoices. Platform operations staff support firms but work within the same tenant boundaries through delegated access.

## Core Concepts

### Accounts and Households and Fee Schedules

**Accounts and Households.** An account is the smallest billable unit. It holds positions at a custodian and has exactly one active fee schedule, either assigned directly or inherited from its household. A household groups related accounts, typically a family, so that their combined AUM can qualify for lower breakpoint rates. Household aggregation is optional and configured per firm, but most firms enable it because clients expect family-level pricing.

**Fee Schedules.** A fee schedule defines how the annual fee rate is computed from AUM. The platform supports flat, tiered, and breakpoint schedules. Schedules are versioned: editing a schedule creates a new version that applies from a chosen effective date, and completed runs keep a reference to the version they used.

## The Billing Cycle

### From Valuation to Invoice and Who Is Involved

**From Valuation to Invoice.** Each billing cycle follows the same path. First, AUM is captured for every billable account as of the valuation date. Second, a billing run is created for the period and processes each account: it resolves the fee schedule, aggregates household AUM if applicable, applies proration for partial periods, and computes the fee. Third, validation checks run, and any blocking problem fails the run with a failure code. Finally, a completed run generates invoices and, where configured, custodian fee files.

**Who Is Involved.** Operations users (OPS) usually start and monitor runs. Advisors review client-level results. Firm administrators approve invoices and own the fee schedule catalog. Read-only users can view results for reporting and audit purposes but cannot change anything.
