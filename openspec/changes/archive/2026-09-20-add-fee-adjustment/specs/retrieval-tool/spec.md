# Spec Delta

## MODIFIED Requirements

### Requirement: Stateless MCP server
The retrieval service SHALL be an MCP server targeting protocol revision
2026-07-28 over Streamable HTTP, operating statelessly without a session id.
It SHALL authenticate callers with the bearer token and derive the principal
from it. A tool that needs a second call to finish its work SHALL carry what it
needs between the calls in the state it hands the caller, never in memory the
server keeps.

#### Scenario: Tool listing
- **WHEN** an authenticated client lists tools
- **THEN** exactly `search_documents`, `get_billing_run_status`, `search_billing_runs`, and `propose_fee_adjustment` are returned

#### Scenario: No session state
- **WHEN** two consecutive requests are sent without any session identifier
- **THEN** both succeed and neither depends on state from the other

#### Scenario: A confirmation lands on another replica
- **WHEN** a proposal is made against one replica and its confirmation is sent to another
- **THEN** the confirmation is honoured, because everything it needs travels in the state

### Requirement: Disambiguating tool descriptions
The `search_documents` description SHALL state when to use it (how, why, what
is the procedure, explain a term) and when not to (current data such as run
status, accounts, fees), naming `get_billing_run_status` and
`search_billing_runs` for those. The stub tools' descriptions SHALL likewise
disambiguate from `search_documents`. The write tool's description SHALL state
that it proposes a change and does not make one, and SHALL name
`search_documents` for questions about how adjustments work.

#### Scenario: Description content
- **WHEN** the tool list is inspected
- **THEN** the `search_documents` description contains a "use when" and a "do not use for" section and names both billing tools

#### Scenario: The write tool says it only proposes
- **WHEN** the tool list is inspected
- **THEN** `propose_fee_adjustment`'s description says it proposes an adjustment for confirmation and points questions about procedure to `search_documents`

### Requirement: Read-only annotations
The reading tools — `search_documents`, `get_billing_run_status` and
`search_billing_runs` — SHALL be annotated readOnlyHint=true,
idempotentHint=true, destructiveHint=false, openWorldHint=false. A tool that
changes data SHALL NOT claim any of those: `propose_fee_adjustment` SHALL be
annotated readOnlyHint=false, destructiveHint=true, idempotentHint=false,
openWorldHint=false.

#### Scenario: Annotations present
- **WHEN** the tool list is inspected
- **THEN** each reading tool carries exactly those annotation values

#### Scenario: The write tool is not disguised
- **WHEN** the tool list is inspected
- **THEN** `propose_fee_adjustment` is not annotated read-only or idempotent, and is annotated destructive

### Requirement: Billing stub tools
`get_billing_run_status` and `search_billing_runs` SHALL be read-only, backed
by seed data scoped to the caller's firm, and return purpose-built result
shapes. Free-text note fields of billing records MUST NOT be part of their
output. The same rule SHALL hold for accounts: an account's free-text note
MUST NOT be part of any tool's output.

#### Scenario: Run status
- **WHEN** a firm A user calls `get_billing_run_status` for run 4417 owned by firm A
- **THEN** the run's status, period, and failure reason (if any) are returned without the note field

#### Scenario: Run of another firm
- **WHEN** a firm A user requests a run owned by firm B
- **THEN** the tool reports that the run was not found

#### Scenario: An account's note never leaves
- **WHEN** a proposal names an account whose seed record carries a note
- **THEN** the note is in no part of the result, including the confirmation summary
