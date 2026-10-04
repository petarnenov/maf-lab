# Spec Delta

## ADDED Requirements

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
