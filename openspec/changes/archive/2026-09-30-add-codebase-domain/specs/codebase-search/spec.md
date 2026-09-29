# Spec Delta

## ADDED Requirements

### Requirement: The codebase search is offered to the chat agent
The api SHALL be configured with the codebase server as the `codebase` domain. The only tool of that server offered
to the agent SHALL be `search_codebase`. The agent's answer SHALL cite code as `path:start-end`, as the snippets
give it.

#### Scenario: A code question answered in the chat
- **WHEN** the user asks "how does the code make a tool call idempotent?"
- **THEN** the chat answer is built from search_codebase snippets and cites their places, instead of refusing
