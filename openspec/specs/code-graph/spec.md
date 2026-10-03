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
