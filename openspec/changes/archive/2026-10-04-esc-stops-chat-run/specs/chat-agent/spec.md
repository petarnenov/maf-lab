# Spec Delta

## ADDED Requirements

### Requirement: A stopped run stops its work
When a chat run's request ends before the run does — CopilotKit's runtime aborting the run on a stop, or the caller
going away — the agent SHALL stop the run's work, and each party working for the run SHALL be told only by its own
protocol's means:
- every tool call in flight on an MCP server SHALL be cancelled through the MCP SDK's own cancellation, as the MCP
  transport defines it; no request or tool of this system's own SHALL be added for it;
- a consultation of an A2A agent in flight SHALL be cancelled as "A stopped consultation is cancelled over there"
  (a2a-client) says;
- calls the run makes to its models and other HTTP services SHALL be abandoned with the run.

The stopped run SHALL record no turn and SHALL leave the conversation as it was before the run, and the stop SHALL be
recorded in the tool audit as the outcome of every tool call it interrupted, with no message content.

#### Scenario: A search in flight is cancelled on its server
- **WHEN** the run is aborted while `search_documents` is running on the retrieval server
- **THEN** the call is cancelled on that server through the MCP SDK's cancellation and does not run to its end

#### Scenario: No turn is recorded
- **WHEN** a run is aborted before it finishes
- **THEN** the conversation has no turn for that run, and the next message continues the conversation as it was

#### Scenario: The interrupted call is audited
- **WHEN** the run is aborted while a tool call is running
- **THEN** the tool audit has an entry for that call with a cancelled outcome and its duration, and no query text
