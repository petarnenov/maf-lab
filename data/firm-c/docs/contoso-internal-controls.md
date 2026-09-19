# Contoso Advisors Billing Internal Controls

## Control Objectives

The internal controls described here protect the accuracy of Contoso Advisors client billing. The firm is small, so segregation of duties is achieved by splitting preparation and approval between the operations associate and the operations lead. The objectives are simple: every household must be on the correct schedule, every invoice must match the client agreement, and every adjustment must have documented approval. Control evidence is kept in the quarterly billing binder, indexed by internal reference CONTOSO-CANARY-2290, and is reviewed during the annual compliance examination.

## Key Controls

### Schedule Assignment Review

Each quarter, before the run is started, the operations associate exports the list of accounts and their assigned schedules. The operations lead compares the export to the client agreement register. Any account without a schedule is fixed before the run so that the run does not fail with FS-REQUIRED. Accounts assigned to a schedule other than CONTOSO-FLAT-100 must have a documented reason.

### Invoice Review

After the run completes, the operations lead reviews every invoice above a threshold amount and a random sample of the rest. The review checks the installment amount, any prorated amounts, and any household rebalancing fee adjustments. Discrepancies are corrected with a billing credit or a debit adjustment, never by editing a completed run.

### Custodian Reconciliation

Within five business days after fees are debited, the operations associate reconciles the custodian fee deductions against the platform invoices. Differences larger than one dollar are investigated and documented. Unresolved items are escalated to the operations lead and tracked until closed.
