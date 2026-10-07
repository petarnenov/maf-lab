# Proposal

## Why

The assistant's outbound A2A client registration is still the compliance reviewer's own configuration section
(`Compliance:BaseUrl`, `Compliance:ClientId`, `Compliance:ClientSecret` in `compose/env/compliance.env`), and the
shared A2A library's task-store keyspace defaults to `compliance`, the live Redis data of the reviewer's tasks. Moved
out of extract-a2a-plugin (its task 2.1): a cross-plugin configuration rename with a live keyspace at stake, which
nothing in that move depended on, reviewed by the compliance plugin's owner.

## What Changes

- The deployment's outbound A2A client registrations generalize from `Compliance:*` to `A2A:Clients:<agent>` (client
  id and secret per agent consulted), mirroring `A2A:Partners:<id>` for the inbound side, in `compose/env/a2a.env`.
  The endpoint defaults from the agent's manifest (`[agent]`, or its card) through the catalogue, as an MCP plugin's
  endpoint does. Client credentials are issued per authorization server, so they stay the deployment's configuration,
  not a plugin folder's.
- The compliance plugin's consultant, the topology probe and the eval host read the registration; `Compliance:*` is
  read as a fallback for one release, then dropped.
- `A2AOptions.StoreKeyspace` loses its default: each service that stores A2A tasks names its keyspace (`compliance`
  for the reviewer, unchanged, so its live tasks stay where they are; `testgen` for the test agent, as today).
- The test agent's client keys (`TestAgent__*`) move to `A2A:Clients:test-agent` (coverage's D6).

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `a2a-client`: the assistant's registration at another agent is `A2A:Clients:<agent>`.

## Principles

- SOLID: one registration shape for every agent consulted (open/closed: a new agent is configuration, not code); the
  consultant depends on the registration, not on a section named after one agent.
- Standards: OAuth 2.0 client credentials (RFC 6749 §4.4), one registration per client per authorization server; .NET
  configuration binding (`IOptions<T>`, the `__` environment separator).

## Progress

None — a configuration change; no work of its own.

## Stopping

None — a configuration change; nothing it changes runs long.

## Documentation impact

- `docs/configuration.md` or README's configuration section, `compose/env/a2a.env`, DECISIONS (§81 part K's
  follow-up).
