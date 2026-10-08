# Proposal

**Risk tier: HIGH** — it concerns the compose topology's configuration of the agent's domain servers: which tools a
chat turn is offered in Docker (docs/rules/openspec-models.md §2, "compose/lb topology").

This change documents, after the fact, the fix committed as `e5a4296` on 2026-10-08. The code is already on `main`;
the change gives it the proposal, the delta spec and the tasks every change in this lab has, so that the main spec
states the contract the fix introduced and the archive records why.

## Why

Right after `add-bulgarian-history-domain` was archived, every history question in the chat ended with "tool does
not exist": the model asked for `search_bulgarian_history`, the turn had loaded the history domain, its server had
answered `tools/list`, and the tool was still not offered. The agent's domain servers are configured twice —
`appsettings.json` for `make dev`, and the compose file's `Agent__Servers__N__*` environment for Docker — and .NET
configuration merges arrays by index: the environment overrides `Domain` and `Endpoint` of entry N and inherits
whatever else entry N holds in the JSON. The history server was entry 1 in the JSON and entry 2 in compose, so in
Docker it came up with the codebase entry's `Tools` allowlist and its only tool was dropped without a log line. Local
in-process runs (`make ask`, the evals) bind the JSON alone and never saw it.

## What Changes

- **The two server lists agree by index.** `appsettings.json` lists the agent's servers in the compose order
  (portfolio, codebase, bulgarian-history), so an entry overridden by the environment inherits only its own JSON
  settings.
- **A test binds the merged configuration the way the api does** (`AgentServersConfigurationTests`): the compose
  file's `Agent__*` environment layered over `appsettings.json`, bound to `AgentOptions`. It fails when an index names
  two different domains, or when a `Tools` allowlist lands on any server but the codebase one. With the old order it
  fails with a message that names the index and the two domains.
- **The requirement is written down**: a server configured with a tools allowlist keeps it to itself; no other
  domain's server is narrowed by it, in Docker or in `make dev`.
- **DECISIONS §81** records the pitfall (already in `e5a4296`).

Not in this change: a log line when a server's tool is left out by its allowlist. It would have made this visible at
once, but it is a separate observability decision; listed as a follow-up in the design.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `chat-agent`: "Tools from every domain server" — a tools allowlist applies only to the server it is configured on,
  whatever source configured it; the servers' two configuration sources name the same domain at each position.

## Impact

- `src/Maf.Lab.Api/appsettings.json`: the order of `Agent:Servers` (two lines swapped).
- `tests/Maf.Lab.Tests/AgentServersConfigurationTests.cs`: new, two tests.
- `DECISIONS.md` §81: one bullet.
- No runtime code changes; no package version moves; the compose file is unchanged.

## Principles

- SOLID: open/closed — the contract is a test over the two existing configuration sources, not a new code path; a fifth
  server is one more entry in both files and the test covers it unchanged. Single responsibility — the test checks
  configuration shape only; `ToolSource` keeps its allowlist behaviour.
- Standards: Microsoft.Extensions.Configuration's own layering and array-by-index binding, used as designed; the fix is
  ordering the data for it, not a mechanism around it. A test that binds the production configuration is the ASP.NET
  Core guidance for configuration contracts.
- Own: nothing new of the project's own. The pitfall and the rule "same order in both files" are recorded in DECISIONS §81.

## Progress

None — the change alters no CLI tool, make target or UI action; a unit test runs in under a second.

## Stopping

None — the change adds no work a person can start; the test runs under `dotnet test`, which Ctrl+C stops as it
always did.

## Documentation impact

- `DECISIONS.md` §81: the bullet on the index merge (in `e5a4296`).
- `README.md`, `CLAUDE.md`, `docs/*.md`, `openspec/project.md`, `.github/copilot-instructions.md`: nothing becomes
  untrue; no document describes the order of `Agent:Servers`. `make docs-check` passes as is.
