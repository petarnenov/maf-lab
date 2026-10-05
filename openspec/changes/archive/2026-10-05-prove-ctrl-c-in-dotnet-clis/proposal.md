# Proposal

## Why

`make eval` calls paid models, and Ctrl+C is how a developer stops it. In the three .NET tools (Eval, Indexing,
A2AProbe) Ctrl+C and SIGTERM are caught by two different lines: `Console.CancelKeyPress` for Ctrl+C and
`PosixSignalRegistration` for SIGTERM. `CliCancelTests` sends only SIGTERM, because a process started in the
background by a non-interactive parent inherits SIGINT as ignored and .NET keeps it so. The Ctrl+C line is therefore
covered by no test: if it were removed or broken, the suite would still pass, and Ctrl+C would end the tool with the
runtime's default exit, with no safe point and no cancel of the server work it started.

## What Changes

- `CliCancelTests` also interrupts each of the three .NET tools with a real SIGINT. The tool is started through a small
  launcher that sets SIGINT back to its default disposition before it runs the tool, so the tool receives Ctrl+C as it
  would from a terminal.
- Each SIGINT test asserts what the SIGTERM test asserts: exit code 130 and the tool's own "cancelled" last line. That
  line is written only by the tool's handler, so the test fails if the Ctrl+C line is gone (the runtime's default exit
  is also 130, so the exit code alone would not tell).
- The class comment that says SIGINT "cannot be relied on here" is corrected.
- No production code changes. Both handlers call the same `cts.Cancel()`, so what follows a stop (the safe point, the
  cancel of server work) is the path the SIGTERM tests already prove.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `stop-anything`: "Ctrl+C stops what a tool started" gains a scenario saying a .NET tool stopped with Ctrl+C stops
  through its own handler, not the runtime's default exit.

## Impact

- `tests/Maf.Lab.Tests/CliCancelTests.cs`: three SIGINT tests and the launcher.
- The launcher is `python3`, already required by `make docs` and present on the CI runners (`ubuntu-24.04`).

## Progress

None — it adds tests only: no tool, target or page action changes.

## Stopping

None — it adds tests only: no work a person can start changes; it proves an existing stop.

## Documentation impact

None. CLAUDE.md, openspec/project.md and .github/copilot-instructions.md already say Ctrl+C or SIGTERM stops a CLI
tool; none of them says how that is tested.
