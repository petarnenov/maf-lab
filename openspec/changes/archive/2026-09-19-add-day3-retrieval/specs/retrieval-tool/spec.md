# Spec Delta

## Purpose

Exposes document retrieval and read-only billing data to agents through an
MCP server, with tool contracts designed so a model can pick the right tool
and never receives more data than it needs.

## ADDED Requirements

### Requirement: Stateless MCP server
The retrieval service SHALL be an MCP server targeting protocol revision
2026-07-28 over Streamable HTTP, operating statelessly without a session id.
It SHALL authenticate callers with the bearer token and derive the principal
from it.

#### Scenario: Tool listing
- **WHEN** an authenticated client lists tools
- **THEN** exactly `search_documents`, `get_billing_run_status`, and `search_billing_runs` are returned

#### Scenario: No session state
- **WHEN** two consecutive requests are sent without any session identifier
- **THEN** both succeed and neither depends on state from the other

### Requirement: search_documents contract
`search_documents` SHALL accept `query` (a natural-language phrase), optional
`sourceTypes` (array of the known source types), and optional `maxResults`
(integer, capped at 10). It SHALL return `results[]` of {snippet, sourcePath,
sectionPath, score, updatedAt, docId}, `totalMatches`, `truncated`, and
`refineHint`. It SHALL return snippets only and never a synthesized answer.

#### Scenario: Cap on results
- **WHEN** `maxResults` is 50
- **THEN** at most 10 results are returned and `truncated` is true if more matched

#### Scenario: Source type filter
- **WHEN** `sourceTypes` is ["code"]
- **THEN** every result has a code source

#### Scenario: Identifier instead of phrase
- **WHEN** `query` is only an identifier such as "4417"
- **THEN** the tool returns a result with a `refineHint` pointing to the billing-run tools

### Requirement: Disambiguating tool descriptions
The `search_documents` description SHALL state when to use it (how, why, what
is the procedure, explain a term) and when not to (current data such as run
status, accounts, fees), naming `get_billing_run_status` and
`search_billing_runs` for those. The stub tools' descriptions SHALL likewise
disambiguate from `search_documents`.

#### Scenario: Description content
- **WHEN** the tool list is inspected
- **THEN** the `search_documents` description contains a "use when" and a "do not use for" section and names both billing tools

### Requirement: Read-only annotations
All three tools SHALL be annotated readOnlyHint=true, idempotentHint=true,
destructiveHint=false, openWorldHint=false.

#### Scenario: Annotations present
- **WHEN** the tool list is inspected
- **THEN** each tool carries exactly those annotation values

### Requirement: Billing stub tools
`get_billing_run_status` and `search_billing_runs` SHALL be read-only, backed
by seed data scoped to the caller's firm, and return purpose-built result
shapes. Free-text note fields of billing records MUST NOT be part of their
output.

#### Scenario: Run status
- **WHEN** a firm A user calls `get_billing_run_status` for run 4417 owned by firm A
- **THEN** the run's status, period, and failure reason (if any) are returned without the note field

#### Scenario: Run of another firm
- **WHEN** a firm A user requests a run owned by firm B
- **THEN** the tool reports that the run was not found

### Requirement: Safe error results
Tool failures SHALL be returned as error results with short, actionable text.
Error text MUST NOT contain stack traces, hostnames, SQL, or query internals.

#### Scenario: Vector store down
- **WHEN** `search_documents` is called while the vector store is unreachable
- **THEN** the result is marked as an error with text like "Document search is temporarily unavailable; try again shortly" and nothing else
