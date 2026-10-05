# Spec Delta

## ADDED Requirements

### Requirement: Tenant and user data can be exported and deleted everywhere
Every store of tenant or user data, in the core or in a plugin, SHALL support export, deletion per tenant, deletion per
user, and a retention policy per tenant. Deleting a user (from SCIM or by request) and offboarding a tenant SHALL reach
every installed plugin. They SHALL run as stoppable, resumable jobs that show their progress. Audit records SHALL
outlive an erasure without content, naming the user only by a pseudonymous id.

#### Scenario: A legal hold
- **WHEN** a tenant is under legal hold and its offboarding or a retention run starts
- **THEN** it refuses and names the hold, and nothing covered by the hold is deleted

#### Scenario: The ledger survives an erasure
- **WHEN** a user who confirmed fee adjustments is erased
- **THEN** the adjustments remain, with the user replaced by a pseudonymous id

#### Scenario: A user leaves
- **WHEN** SCIM deletes a user
- **THEN** that user's conversations, feedback and plugin data are gone from every store, other users' data is
  untouched, and the audit keeps only a pseudonymous id

#### Scenario: A deletion is stopped
- **WHEN** a tenant offboarding is stopped halfway and started again
- **THEN** it resumes, finishes, and leaves no data of that tenant
