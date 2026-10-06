# Spec Delta

## MODIFIED Requirements

### Requirement: Code snippets for a chat question
While the `code` plugin is installed, the api SHALL offer an authenticated endpoint that returns the codebase snippets
for a question; without it, the route SHALL answer 404. It SHALL call search_codebase as the calling user, forwarding
the user's token, and return the same snippet shape. If the codebase server cannot be reached, the endpoint SHALL answer
with a short error that names no host.

#### Scenario: Snippets for a question
- **WHEN** the web asks the endpoint for the question "where are chunks sized for the embedding model?"
- **THEN** it receives snippets from the indexing code with paths and line ranges

#### Scenario: Code server down
- **WHEN** the codebase server is unreachable
- **THEN** the endpoint answers 503 with a message that the code search is unavailable

#### Scenario: The plugin is not installed
- **WHEN** the `code` plugin is not installed and the web asks the endpoint
- **THEN** it answers 404

### Requirement: The codebase search is offered to the chat agent
While the `code` plugin is installed, its manifest's `[domain]` table SHALL make the codebase server the `codebase`
domain, reached at the endpoint its server.json names (or a configured `Agent:Servers:code` override). The tools of
that server offered to the agent SHALL be `search_codebase`, `trace_code_symbol` and `change_impact`. `ask_codebase`,
which writes its own answer, SHALL NOT be offered. The agent's answer SHALL cite code as `path:start-end`, as the
snippets and the graph results give it. Without the plugin, no codebase tool is offered and the prompt has no codebase
wording.

#### Scenario: A code question answered in the chat
- **WHEN** the user asks "how does the code make a tool call idempotent?"
- **THEN** the chat answer is built from search_codebase snippets and cites their places, instead of refusing

#### Scenario: The codebase tools the agent sees
- **WHEN** a turn loads the codebase domain's tools
- **THEN** it offers `search_codebase`, `trace_code_symbol` and `change_impact`, and not `ask_codebase`
