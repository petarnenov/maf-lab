# Roles and Permissions at Contoso Advisors

## Role Assignments

Contoso Advisors uses the standard platform roles, but assigns them narrowly because the firm has a small staff. The managing partner and the operations lead hold FIRM_ADMIN. The operations associate holds OPS. Each advisor holds ADVISOR, and the outside compliance consultant holds READ_ONLY. Role assignments are reviewed every January and whenever someone joins or leaves the firm. Temporary elevation is not allowed; if extra access is needed, the operations lead performs the task instead.

## What Each Role Can Do

### FIRM_ADMIN

FIRM_ADMIN users create and edit fee schedules, including CONTOSO-FLAT-100, approve billing runs, and issue billing credits. They also manage user access. At Contoso Advisors, schedule edits require a second FIRM_ADMIN to review the change before the next run.

### OPS

The OPS role prepares billing runs, assigns existing schedules to accounts, re-runs failed runs, and records household rebalancing fee adjustments. OPS users cannot create schedules or approve credits above the firm threshold.

### ADVISOR and READ_ONLY

Advisors view invoices and billing history for their own households and can request waivers of the household rebalancing fee. READ_ONLY users can see billing data across the firm for review purposes but cannot change anything.

## Access Reviews

The operations lead exports the user list from the platform each quarter and compares it with the staff roster. Access for departed staff is removed on their last day. The review result is filed with the quarterly billing binder.
