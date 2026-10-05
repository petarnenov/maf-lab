# Design

## Context

`CliCancelTests` starts each .NET tool from its own build output against a `BlackHole` (accepts, never answers),
waits until the tool is caught waiting, sends `kill -TERM`, and asserts exit code 130 and the tool's last line. The
tools register `Console.CancelKeyPress` (SIGINT) and `PosixSignalRegistration(SIGTERM)`; both call `cts.Cancel()`.

The test process is started by `dotnet test` without a terminal, so its children inherit SIGINT as ignored. The .NET
runtime leaves a signal that was ignored at start ignored, so a `kill -INT` reaches no handler and the tool does not
stop.

## Goals / Non-Goals

**Goals:**
- A test per .NET tool (Eval, Indexing, A2AProbe) that fails if that tool's Ctrl+C handler is missing or does not
  cancel.

**Non-Goals:**
- Changing the tools or their handlers.
- The Python and Node scripts: they handle SIGINT and SIGTERM with one function, so the SIGTERM tests cover both.
- Proving again what follows `cts.Cancel()` (the safe point, cancelling server work): the SIGTERM tests prove it, and
  it is the same code.

## Decisions

- **A launcher resets SIGINT, then becomes the tool.** The tool is started as
  `python3 -c "<set SIGINT to SIG_DFL; os.execvp(...)>" dotnet <tool> <args>`. `execvp` keeps the process id, so
  `kill -INT <pid>` reaches the tool, and a default disposition survives `exec`, so the runtime installs its handler.
  Alternatives: `sh -c 'trap - INT; exec …'` does not work (POSIX shells cannot reset a signal ignored on entry); a new
  process group or `setsid` changes who gets the signal, not whether it is ignored; `posix_spawn` with
  `POSIX_SPAWN_SETSIGDEF` through P/Invoke works but is far more code. `python3` is already required by `make docs`
  and is on the `ubuntu-24.04` runners.
- **One helper, two signals.** `InterruptAsync` takes the signal (`TERM` or `INT`); for `INT` it starts the tool
  through the launcher. Each tool's test becomes a theory over both signals, so the two cannot drift apart.
- **The last line is the proof, not the exit code.** A process ended by SIGINT's default action also reports 130
  (128 + 2), so the assertion that matters is the tool's own "cancelled" line, written only by its handler.
- **Checked once by breaking it.** Remove Eval's `CancelKeyPress` line, see its SIGINT test fail, put it back.

## Risks / Trade-offs

- [The launcher is missing on a machine] → the SIGINT tests fail at start, naming `python3`, not silently. Every
  machine that runs `make ci` already has it: `docs-check` requires Python 3.11 (`require-python`).
- [A future runner starts tests with SIGINT at default] → the launcher's reset is then a no-op; the test still holds.
