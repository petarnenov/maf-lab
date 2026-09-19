# Roles and Permissions

## Role Model

Access to billing features is controlled by roles assigned to users within a firm. A user's role applies only to the firm they belong to; there is no role that grants access to another firm's data. Platform support staff act on behalf of a firm only through a time-limited delegated session that is logged and visible to the firm's administrators. The four billing roles are FIRM_ADMIN, ADVISOR, OPS, and READ_ONLY. Role changes take effect at the next sign-in and are recorded in the audit log.

## Roles

### FIRM_ADMIN, ADVISOR, OPS and READ_ONLY

**FIRM_ADMIN.** Firm administrators own the fee schedule catalog, approve invoices and credits above configured thresholds, configure billing periods and methods, and manage users. They can void invoices and close billing periods. Most firms have two or three administrators to ensure approvals are not blocked by absences.

**ADVISOR.** Advisors can view billing results for the households and accounts they service, review draft invoices, and request credits or schedule changes for their clients. They cannot start runs, edit schedules, or approve their own requests.

**OPS.** Operations users run the day-to-day billing process. They start and monitor billing runs, investigate failures, assign existing fee schedules to accounts, correct valuations through the approved workflow, and create adjustments and credits within limits. They cannot create new fee schedules or approve corrections above the limit.

**READ_ONLY.** Read-only users can view runs, invoices, schedules, and reports for their firm. The role is intended for compliance reviewers, auditors, and executives who need visibility without the ability to change billing data.

## Separation of Duties

### Recommended Practices

Firms should ensure that the person who requests a credit is not the person who approves it, and that schedule changes are reviewed by a second administrator. The platform enforces that a user cannot approve a request they created, but broader controls such as periodic access reviews remain the firm's responsibility. Firms should also remove the FIRM_ADMIN role from users who change jobs, review the list of delegated support sessions each quarter, and avoid shared accounts, since every billing action is attributed to an individual user in the audit log and shared credentials make that attribution meaningless.
