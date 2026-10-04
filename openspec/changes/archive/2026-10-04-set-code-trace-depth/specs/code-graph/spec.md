# Spec Delta

## MODIFIED Requirements

### Requirement: trace_code_symbol tool
The codebase MCP server SHALL offer a read-only tool `trace_code_symbol`. It SHALL take a symbol name (a type, or a
type and member), a direction (callers or callees) and an optional depth from 1 to 4, which defaults to 4 when
it is not given. It SHALL return the matching
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

#### Scenario: The default depth
- **WHEN** the agent traces the callees of `ChatTurnRunner.RunAsync` without a depth
- **THEN** the trace follows up to 4 calls and the result says it was traced to depth 4

#### Scenario: A shallower trace on request
- **WHEN** the agent traces with depth 2
- **THEN** the trace follows up to 2 calls

#### Scenario: Out of range
- **WHEN** the agent traces with depth 5 or 0
- **THEN** the tool returns an error that depth must be between 1 and 4, and no query runs

#### Scenario: More methods than the limit
- **WHEN** a trace reaches more methods than the node limit
- **THEN** the result holds the nearest methods first, up to the limit, and its truncated flag is set

## ADDED Requirements

### Requirement: Evaluation depth pin up to the trace cap
The codebase MCP server SHALL accept one optional setting that pins the call depth of both graph tools, from 1 to the
deepest depth the graph templates provide (4). The setting is for measurement only. With the trace's cap at 4, every pin is at or
below the tool's own cap.

When it is unset — in every deployed configuration — the tools SHALL behave as their requirements state:
- `trace_code_symbol` takes a depth of at most 4, default 4;
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
- When unset, the published description and schema SHALL be the tool's own: up to 4 calls, and a `depth` argument of 1-4
  with a default of 4.

A value outside the supported range SHALL stop the server from starting, rather than be clamped. The pin SHALL come only
from the server's configuration, never from a request, a tool argument or the model. It SHALL NOT change the tenant
scope, the node limit or the set of graph templates.

#### Scenario: Unset
- **WHEN** the server starts without the pin and the agent traces with depth 5
- **THEN** the tool returns its usual error that depth must be between 1 and 4

#### Scenario: Pinned below the default
- **WHEN** the server runs with the pin at 2 and a caller traces `TenantScopedSearch.QueryAsync`, passing depth 4 anyway
- **THEN** the trace follows up to 2 calls and the result says it was traced to depth 2

#### Scenario: A pinned server describes the pin
- **WHEN** a client lists the tools of a server pinned at 4
- **THEN** the description of `trace_code_symbol` says it reaches methods through up to 4 calls and names no other limit, and its input schema has no `depth` argument

#### Scenario: An unpinned server publishes the tool's own depth
- **WHEN** a client lists the tools of a server without the pin
- **THEN** the description of `trace_code_symbol` says up to 4 calls, its `depth` argument is offered as 1-4 with a default of 4, and `change_impact` is as published

#### Scenario: Impact pinned
- **WHEN** the server runs with the pin at 2 and the agent asks for the impact of a file
- **THEN** callers are followed through at most 2 calls

#### Scenario: Out of range
- **WHEN** the server is configured with the pin at 5 or 0
- **THEN** it does not start, and the error names the setting and the allowed range

#### Scenario: Not settable by a caller
- **WHEN** a tool call carries any argument or metadata that names a depth pin
- **THEN** the pin in effect is still the one from configuration

## REMOVED Requirements

### Requirement: Evaluation depth pin
**Reason**: It described a pin that could go deeper than the trace's public cap of 3, and an unpinned server frozen at
the text published before add-graph-depth-eval. With the cap at 4, neither holds.
**Migration**: Replaced by "Evaluation depth pin up to the trace cap", which keeps the same setting, range, startup check
and published-text rules, and states the unpinned behaviour at depth 4.
