## ADDED Requirements

### Requirement: The monitor shows graph reads
The monitor SHALL show each `graph` event of a turn.

- **Timeline.** Each `graph` event SHALL be its own row, with a category colour of its own that is used by no other
  kind and follows the theme. The row SHALL carry the event's title and the time spent in the graph store as its
  duration.
- **Retrieval view.** For each `graph` event, the view SHALL show a graph card next to the turn's searches, with:
  - the tool and the MCP replica that served it;
  - the tenant scope;
  - the total rows, and the time spent in Neo4j as a `neo4j <ms>` timing chip styled like a search's `qdrant <ms>` chip;
  - the outcome, when it is not `ok`;
  - one row per read, with the template's name, the rows returned out of its limit, whether it was truncated, its
    duration and its outcome.

  A turn with graph reads and no search SHALL show its graph cards instead of the empty state.
- **Header.** The header SHALL count the graph reads among its chips when the turn has any.

Every one of these SHALL follow the time-travel cursor like the rest of the monitor. A stored trace with no `graph`
event, including one recorded before the kind existed, SHALL render as it did before, and a `graph` event missing a
field SHALL render with that field shown as not available rather than failing the view.

#### Scenario: A graph lookup in the timeline
- **WHEN** a person opens the timeline of a turn that called `trace_billing_relationships`
- **THEN** a `graph` row follows that call's `tool.result` row, in the graph colour, titled like
  "Neo4j billing_neighbourhood_2 + firm_runs · 9 rows · 12 ms"

#### Scenario: Graph reads in the retrieval view
- **WHEN** a person opens the retrieval view of that turn
- **THEN** a graph card shows the tool, the replica, the scope, 9 rows, a `neo4j 12 ms` chip, and one row each for
  `billing_neighbourhood_2` and `firm_runs` with their rows, limits and durations

#### Scenario: Graph unavailable in the monitor
- **WHEN** the turn's `graph` event has the outcome `unavailable`
- **THEN** the graph card says the graph store was unavailable, and the read's row shows the outcome

#### Scenario: Before and after the graph step
- **WHEN** the cursor is before the `graph` event
- **THEN** the retrieval view shows no graph card and the header shows no graph-read count, and moving the cursor
  onto the event shows both

#### Scenario: An older stored turn
- **WHEN** a person opens a stored turn whose trace has no `graph` event
- **THEN** the timeline, the retrieval view and the header render as they did before this change
