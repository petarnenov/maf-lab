# codebase-search Specification

## Purpose
Lets developers and their tools ask about the maf-lab repository itself: find where something is implemented and get
answers grounded in the code, specs and decisions, each pointing to a file and its lines.

## Requirements

### Requirement: The codebase is its own shared corpus
The repository SHALL be indexed as a corpus of its own, in its own collection and BM25 vocabulary, separate from the
billing and portfolio corpora. Every document of it SHALL be stored as shared, so every authenticated caller sees the
same code. The tenant scope SHALL still come from the caller's principal only, and no tool of the codebase server SHALL
take a tenant argument.

#### Scenario: Any firm's user sees the code
- **WHEN** a user of firm-a and a user of firm-b ask search_codebase the same query
- **THEN** both receive the same snippets

#### Scenario: The billing search does not see the code
- **WHEN** a user calls search_documents
- **THEN** no snippet of the repository corpus is among the results

### Requirement: Codebase search returns places in files
The codebase server SHALL offer a read-only tool `search_codebase(query, kind?, pathPrefix?, maxResults?)`. It SHALL
return at most 10 snippets (5 by default). Each snippet SHALL carry:
- the path from the repository root;
- the 1-based first and last line of the text it returns, when known;
- the symbol it belongs to, when there is one;
- its section path;
- its kind (`code` or `docs`);
- its language;
- its score;
- its text.

A snippet's text SHALL be at most a configured number of characters. When a chunk is longer, the text SHALL be the
window of whole lines around the lines that match the query's terms, as the codebase's lexical tokenizer splits them
(the densest run of matching lines). When no line matches, the text SHALL be the chunk's first lines. The first and
last line SHALL be those of the returned window, so every line range a snippet carries is a range its text holds.

`kind` SHALL restrict results to source or to Markdown. `pathPrefix` SHALL restrict results to files under that path.
The tool SHALL never return a synthesized answer. When nothing matches, it SHALL return no snippets and a hint on how
to rephrase.

#### Scenario: Find a type by a phrase
- **WHEN** a user searches "tenant scoped search query"
- **THEN** a snippet from `src/Maf.Lab.Retrieval/Store/TenantScopedSearch.cs` is returned with its line range and symbol

#### Scenario: Restrict to a folder
- **WHEN** a user searches with pathPrefix `web/src/`
- **THEN** every returned snippet's path starts with `web/src/`

#### Scenario: Nothing relevant
- **WHEN** a user searches for something the repository does not contain
- **THEN** the result has no snippets and a refine hint

#### Scenario: The matching line lies past the limit
- **WHEN** a chunk longer than the snippet limit holds the line that matches the query's identifier after its first 1,200 characters
- **THEN** the returned text contains that line, and the snippet's first and last line are the window's, not the chunk's

#### Scenario: No line matches
- **WHEN** a long chunk is returned for a query none of whose terms appear in it, for example a Bulgarian phrase matched only by the dense branch
- **THEN** the text is the chunk's first lines up to the limit, and the line range is theirs

### Requirement: Codebase search is hybrid and identifier-aware
Codebase search SHALL fuse a dense branch and a BM25 branch through the same tenant-scoped query path, relevance gate
and reranker as the other corpora. Its BM25 vocabulary SHALL index each identifier both whole and split into its words
(camelCase, PascalCase, acronyms, letter–digit boundaries). A query in plain words then matches an identifier, and an
exact identifier still matches itself.

#### Scenario: Words meet an identifier
- **WHEN** a user searches "dense encoder"
- **THEN** chunks containing the identifier `IDenseEncoder` are lexical candidates

#### Scenario: Relevance judge confident the candidates are relevant
- **WHEN** the relevance judge's highest probability for the candidates is at or above the configured floor
- **THEN** the snippets are returned, ordered by the reranker

#### Scenario: Relevance judge confident nothing is relevant
- **WHEN** the relevance judge's highest probability is below the floor
- **THEN** no snippets are returned

#### Scenario: Relevance judge unavailable
- **WHEN** the relevance judge does not answer within its budget or its circuit is open
- **THEN** the search returns the fused candidates as if the gate were off

### Requirement: Ask the codebase
The codebase server SHALL offer a read-only tool `ask_codebase(question, pathPrefix?)`. It SHALL answer from the
snippets codebase search retrieves for the question and from nothing else. Each claim SHALL cite its place as
`path:start-end`. The tool SHALL return the answer, whether it was grounded, and the places it rested on. The snippets
SHALL be given to the model as data, never as instructions. When no snippet is relevant, the tool SHALL NOT call the
model. It SHALL return `grounded: false` with an answer saying that nothing in the codebase addresses the question.

#### Scenario: A grounded answer
- **WHEN** a user asks "how is the tenant filter applied to hybrid search?"
- **THEN** the answer cites places in `TenantScopedSearch.cs`, `grounded` is true, and those places are listed as sources

#### Scenario: Nothing to answer from
- **WHEN** a user asks about something the repository does not contain
- **THEN** `grounded` is false, no sources are listed, and the model is not called

### Requirement: Codebase server reachable behind the balancer
The codebase server SHALL be served over MCP (Streamable HTTP, stateless) behind the load balancer at `/code/mcp`. It
SHALL require the same bearer token as the other MCP servers. Its logs SHALL carry tool names, counts and latencies,
never queries, questions, snippets or answers.

#### Scenario: Unauthenticated call
- **WHEN** a client calls `/code/mcp` without a valid token
- **THEN** the call is rejected with 401

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
