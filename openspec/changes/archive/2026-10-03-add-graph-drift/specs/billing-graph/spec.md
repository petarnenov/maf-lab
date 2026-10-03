## MODIFIED Requirements

### Requirement: Billing graph model
The billing graph SHALL hold firms, households, accounts, billing runs, fee schedules and documents, built from the
seed data and the billing corpus:
- an account belongs to a firm and, when the seed names one, to a household;
- a billing run belongs to a firm;
- a document node is created for each indexed billing document, keyed by the same document id the vector store uses,
  and it records the hash of the source content it was built from, the same value the vector store records for that
  document's chunks;
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

#### Scenario: Document records its source content
- **WHEN** a billing document is indexed and the graph is built from the same corpus
- **THEN** its document node's source content hash equals the content hash on that document's chunks in the vector
  store, and it changes when the document's text changes

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
