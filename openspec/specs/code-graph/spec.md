# code-graph Specification

## Purpose
Models the repository's C# code as a graph of projects, files, types and methods with call and reference edges. The
codebase MCP server can then answer who calls a symbol and what a file's code is reached from, including by tests.

## Requirements

### Requirement: Code graph model
The code graph SHALL be built from the git-tracked C# files of the repository, analysed semantically rather than by
text patterns. It SHALL hold:
- projects, with their project references;
- files, with the project that contains them;
- types, with the file that declares them;
- methods and constructors, with the type that declares them;
- call edges from a method to each method of the repository it invokes.

Calls into external libraries SHALL NOT create nodes. Every code graph node SHALL have tenant `shared`. Each method
node SHALL carry its file path and line span, matching the line numbers the codebase search reports.

#### Scenario: Call edge resolved
- **WHEN** the graph is built
- **THEN** there is a call edge from `DocumentSearchService`'s search method to `TenantScopedSearch.QueryAsync`

#### Scenario: Test code is part of the graph
- **WHEN** the graph is built
- **THEN** test methods under `tests/` are nodes, with call edges to the production methods they invoke

#### Scenario: Unparseable file
- **WHEN** a C# file fails to compile in isolation
- **THEN** the build still adds the file and the declarations it can resolve, counts the unresolved calls in the
  summary, and does not fail

### Requirement: trace_code_symbol tool
The codebase MCP server SHALL offer a read-only tool `trace_code_symbol`. It SHALL take a symbol name (a type, or a
type and member), a direction (callers or callees) and an optional depth, at most 3. It SHALL return the matching
symbols and, for each, the methods reached in that direction, each with its file path and line span. The result
SHALL have a node limit and a truncated flag. When the name is ambiguous, the tool SHALL list the candidates instead of
guessing.

#### Scenario: Callers of a method
- **WHEN** the agent traces the callers of `TenantScopedSearch.QueryAsync`
- **THEN** the result lists the calling methods with their files and line spans

#### Scenario: Ambiguous name
- **WHEN** the agent traces `Program.Main`
- **THEN** the result lists one candidate per project and traces none of them

#### Scenario: Unknown symbol
- **WHEN** the agent traces a name that is not in the graph
- **THEN** the tool returns an empty result that says no symbol matched and suggests `search_codebase`

### Requirement: change_impact tool
The codebase MCP server SHALL offer a read-only tool `change_impact`. It SHALL take a repository-relative file path and
return the methods declared in that file, the methods that reach them through calls up to a bounded depth, and, among
those, the test methods, grouped by test file. Paths outside the repository or not in the graph SHALL get a short
error.

#### Scenario: Impact of a production file
- **WHEN** the agent asks for the impact of `src/Maf.Lab.Retrieval/Store/TenantScopedSearch.cs`
- **THEN** the result includes the tests in `tests/Maf.Lab.IntegrationTests/TenancyAcceptanceTests.cs` that reach it

#### Scenario: Path escapes the repository
- **WHEN** the path is `../etc/passwd` or an absolute path
- **THEN** the tool returns a short error, and no query runs

### Requirement: Code graph tool descriptions disambiguate
The descriptions of `trace_code_symbol` and `change_impact` SHALL say to use them for structural questions (who calls
what, what a change affects) and not for questions about what code says or how it works, naming `search_codebase` and
`ask_codebase` for those.

#### Scenario: Description content
- **WHEN** the codebase server's tool list is inspected
- **THEN** both descriptions have a "use when" and a "do not use for" section naming `search_codebase`

#### Scenario: Selection eval
- **WHEN** the selection suite runs its structural cases, such as "who calls TenantScopedSearch.QueryAsync?" and
  "what tests cover TenantScopedSearch.cs?"
- **THEN** the agent calls `trace_code_symbol` or `change_impact` respectively

### Requirement: Evaluation depth pin
The codebase MCP server SHALL accept one optional setting that pins the call depth of both graph tools, from 1 to the
deepest depth the graph templates provide (4). The setting is for measurement only.

When it is unset — in every deployed configuration — the tools SHALL behave as their requirements state:
- `trace_code_symbol` takes a depth of at most 3, default 2;
- `change_impact` follows callers to its bounded depth.

When it is set:
- `trace_code_symbol` SHALL trace to the pinned depth (every method within that many calls), whatever depth the
  caller passed;
- `change_impact` SHALL follow callers to the pinned depth;
- the trace result SHALL state the depth it was traced to, as it already does. The impact result's shape SHALL NOT
  change.

Everything the server tells a caller SHALL be true for the depth in effect:
- When pinned, the published description of `trace_code_symbol` SHALL state that it reaches methods through up to the
  pinned number of calls, and SHALL name no other limit.
- When pinned, its published input schema SHALL NOT offer a `depth` argument, because the caller can no longer choose
  one.
- The description of `change_impact` names no number of calls, and SHALL stay so, so that it holds at any depth.
- When unset, every published description and schema SHALL be exactly what it is today.

A value outside the supported range SHALL stop the server from starting, rather than be clamped. The pin SHALL come only
from the server's configuration, never from a request, a tool argument or the model. It SHALL NOT change the tenant
scope, the node limit or the set of graph templates.

#### Scenario: Unset
- **WHEN** the server starts without the pin and the agent traces with depth 4
- **THEN** the tool returns its usual error that depth must be between 1 and 3

#### Scenario: Pinned deeper than the public cap
- **WHEN** the server runs with the pin at 4 and a caller traces `TenantScopedSearch.QueryAsync`, passing depth 2 anyway
- **THEN** the trace follows 4 calls and the result says it was traced to depth 4

#### Scenario: A pinned server describes the pin
- **WHEN** a client lists the tools of a server pinned at 4
- **THEN** the description of `trace_code_symbol` says it reaches methods through up to 4 calls and names no other limit, and its input schema has no `depth` argument

#### Scenario: An unpinned server is unchanged
- **WHEN** a client lists the tools of a server without the pin
- **THEN** the description and input schema of `trace_code_symbol` and `change_impact` are identical to those published before this change

#### Scenario: Impact pinned
- **WHEN** the server runs with the pin at 2 and the agent asks for the impact of a file
- **THEN** callers are followed through at most 2 calls

#### Scenario: Out of range
- **WHEN** the server is configured with the pin at 5 or 0
- **THEN** it does not start, and the error names the setting and the allowed range

#### Scenario: Not settable by a caller
- **WHEN** a tool call carries any argument or metadata that names a depth pin
- **THEN** the pin in effect is still the one from configuration
