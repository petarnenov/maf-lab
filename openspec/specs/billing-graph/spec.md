# billing-graph Specification

## Purpose
Models the billing domain as a graph of firms, households, accounts, billing runs, fee schedules and documents, so
the agent can
answer relationship questions with the `trace_billing_relationships` tool, within the caller's firm.

## Requirements

### Requirement: Billing graph model
The billing graph SHALL hold firms, households, accounts, billing runs, fee schedules and documents, built from the
seed data and the billing corpus:
- an account belongs to a firm and, when the seed names one, to a household;
- a billing run belongs to a firm;
- a document node is created for each indexed billing document, keyed by the same document id the vector store uses;
- a document mentions an account or a household when its text contains that entity's id;
- a fee schedule is created for each distinct fee schedule code (such as `NW-INST-2026-083`) found in a document, with
  the tenant of that document; the same code in two tenants is two separate fee schedules;
- a document mentions a fee schedule when its text contains that schedule's code.

Mentions SHALL be found by matching known ids and one fixed fee schedule code pattern, not by a model. Free-text note fields from the seed data MUST NOT be
written to the graph.

#### Scenario: Account linked to household and firm
- **WHEN** the graph is built from the seed data
- **THEN** account `A-1042` is linked to its firm and to household `HH-RIDGELINE`, as in the seed

#### Scenario: Document linked by id
- **WHEN** a firm A procedure document contains the id of a firm A account
- **THEN** the document node is linked to that account with a mention edge, and it carries the same document id as
  its chunks in the vector store

#### Scenario: Fee schedule linked from documents
- **WHEN** a firm B household profile and the firm B fee schedule note both contain `NW-INST-2026-083`
- **THEN** there is one firm B fee schedule `NW-INST-2026-083`, and both documents are linked to it by mention edges

#### Scenario: Reference codes are not fee schedules
- **WHEN** a document contains an internal reference code such as `NW-CANARY-7731-HH0005`
- **THEN** no fee schedule is created for it

#### Scenario: Notes never reach the graph
- **WHEN** the graph is built from seed records whose notes contain injection or canary text
- **THEN** no node or edge property contains that text

#### Scenario: Shared documents stay shared
- **WHEN** a shared fee-schedule document is added to the graph
- **THEN** its node has tenant `shared`, and it is linked only to entities whose id it mentions

### Requirement: trace_billing_relationships tool
The billing MCP server SHALL offer a read-only tool `trace_billing_relationships`. It SHALL take an account id,
a household id or a fee schedule code, and an optional depth, at most 2. It SHALL return the entity and its
neighbourhood within the caller's firm and the shared corpus: for an account, its firm, household, the household's
other accounts and the firm's latest billing runs; for a fee schedule, the documents that mention it and, at depth 2,
the other fee schedules those documents mention; and for any entity, the documents that mention it. Each document SHALL carry its document id, title and source type,
so the agent can follow up with `search_documents`. The result SHALL be a purpose-built structure with a node limit
and a truncated flag. It SHALL contain no free-text notes and no fee amounts beyond what `get_billing_run_status` and
the account tools already return.

#### Scenario: Household neighbourhood
- **WHEN** an advisor of firm A traces household `HH-RIDGELINE`
- **THEN** the result lists the household's firm A accounts and the documents that mention them, all with tenant
  firm A or shared

#### Scenario: Fee schedule neighbourhood
- **WHEN** an advisor of firm B traces `NW-INST-2026-083`
- **THEN** the result lists the firm B fee schedule note and the household profiles that mention it, each with its
  document id

#### Scenario: Another firm's id
- **WHEN** a firm A user traces an account id or a fee schedule code that exists only in firm B
- **THEN** the tool answers exactly as it does for an unknown id, revealing nothing about firm B

#### Scenario: Depth is capped
- **WHEN** the tool is called with depth 5
- **THEN** the argument is rejected with a short error naming the allowed range, or clamped to 2, and the result never
  goes deeper than 2

#### Scenario: No tenant argument
- **WHEN** the tool's input schema is inspected
- **THEN** it has no tenant or firm field

### Requirement: Billing graph tool description disambiguates
The description of `trace_billing_relationships` SHALL say to use it for how billing entities connect: which accounts
share a household, which documents concern an account or a household, and which households and documents refer to a
fee schedule. It SHALL say not to use it for procedures,
naming `search_documents`, or for current run status, naming `get_billing_run_status` and `search_billing_runs`.

#### Scenario: Description content
- **WHEN** the tool list is inspected
- **THEN** the description has a "use when" and a "do not use for" section that names `search_documents`,
  `get_billing_run_status` and `search_billing_runs`

#### Scenario: Selection eval
- **WHEN** the selection suite runs its relationship cases, such as "which households use fee schedule
  NW-INST-2026-083?" and "which accounts are in the same household as A-1042?", in English and Bulgarian
- **THEN** the agent calls `trace_billing_relationships`, and it does not call it for the existing procedural cases
