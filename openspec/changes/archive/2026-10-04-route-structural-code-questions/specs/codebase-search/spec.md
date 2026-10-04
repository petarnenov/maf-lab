# Spec Delta

## MODIFIED Requirements

### Requirement: The codebase search is offered to the chat agent
The api SHALL be configured with the codebase server as the `codebase` domain. The tools of that server offered to the
agent SHALL be `search_codebase`, `trace_code_symbol` and `change_impact`. `ask_codebase`, which writes its own answer,
SHALL NOT be offered. The agent's answer SHALL cite code as `path:start-end`, as the snippets and the graph results give
it.

#### Scenario: A code question answered in the chat
- **WHEN** the user asks "how does the code make a tool call idempotent?"
- **THEN** the chat answer is built from search_codebase snippets and cites their places, instead of refusing

#### Scenario: The codebase tools the agent sees
- **WHEN** a turn loads the codebase domain's tools
- **THEN** it offers `search_codebase`, `trace_code_symbol` and `change_impact`, and not `ask_codebase`
