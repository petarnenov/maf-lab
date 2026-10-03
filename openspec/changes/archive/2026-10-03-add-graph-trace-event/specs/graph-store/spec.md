## MODIFIED Requirements

### Requirement: Graph logs carry structure only
Graph reads and writes SHALL be logged and traced with the query name, duration, row and node counts and the outcome,
never with node properties, document text or argument values that came from a chat message. This SHALL hold for the
turn trace as well: the read path SHALL record each read for the turn trace at the point where it binds the tenants,
from the same template name, row count, truncation, duration and outcome that its span carries, and with nothing
more than those and the tenants it bound.

#### Scenario: A traced graph lookup
- **WHEN** a graph tool runs during a chat turn
- **THEN** its span carries the query name, duration and result count, and no account names, ids from the message, or
  document text

#### Scenario: The turn trace's graph event matches the span
- **WHEN** a graph tool runs during a chat turn with diagnostics requested
- **THEN** the turn trace's `graph` event names the same templates, with the same row counts and truncation as their
  `graph.read` spans, and holds no id from the message, no node property and no document text
