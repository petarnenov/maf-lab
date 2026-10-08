# Design

## Context

See proposal.md — Why. `Program.cs` binds `Agent` from the usual ASP.NET Core sources: `appsettings.json`, then
environment variables, the later overriding the earlier key by key. An array binds as keys `Agent:Servers:0:Domain`,
`Agent:Servers:0:Endpoint`, `Agent:Servers:1:Tools:0`, … so an environment that sets `Agent__Servers__2__Domain` and
`Agent__Servers__2__Endpoint` replaces those two keys of entry 2 and leaves `Agent:Servers:2:Tools:*` from the JSON
in place. `ToolSource` then offers, from a server with a non-empty `Tools`, only the tools named in it, silently.

The fix is already on `main` (`e5a4296`); this design records the choice it made.

## Goals / Non-Goals

**Goals:**
- One rule, stated and tested: the two configuration sources list the servers in the same order.
- The test binds the real files the way the api does, so a future entry added to one file only fails before Docker.

**Non-Goals:**
- Changing how servers are configured (a dictionary keyed by domain, a single source) — see Decisions.
- A log line for a tool left out by an allowlist — a separate observability change.

## Decisions

- **Keep the array, order the JSON after compose.** Alternatives: (a) key the servers by domain
  (`Agent:Servers:portfolio:Endpoint`) so no index can collide — a dictionary binds cleanly, but it changes the shape
  every compose file, launch profile and test fixture use, for a bug that a two-line swap and a test close; (b) drop the
  server list from `appsettings.json` and configure `make dev` through the environment only — it moves the dev
  configuration out of the place the launch profiles and the README point to; (c) clear the inherited `Tools` in
  compose with an empty `Agent__Servers__2__Tools__0` — environment variables cannot remove a key, only set one, so it
  would not work. The swap is the smallest change that is correct, and the test keeps it correct.
- **The test binds the merged configuration, not just the order.** Comparing the two files' domain order catches the
  cause; binding the merge and asserting where `Tools` lands catches the effect, including a future allowlist added to
  another server in only one file. Both run; the first fails with the position and the two domains.
- **Compose is read with a regular expression, not a YAML parser.** The test project has no YAML dependency; the api
  service's environment block is flat `key: value` lines, and adding a package for this would move DECISIONS.

## Risks / Trade-offs

- [The regular expression stops matching if the api service block changes shape] → the test asserts the block is
  non-empty and the domain map is non-empty, so a silent no-op fails loudly.
- [A fourth source (`launchSettings.json`, `.vscode/launch.json`) configures servers one day] → it would not be covered;
  the DECISIONS bullet states the rule for every source.

## Migration Plan

Already deployed: `make up` rebuilt the api with the reordered JSON; the history question answered from its search.
Rollback is the reverse swap, which the test refuses.
