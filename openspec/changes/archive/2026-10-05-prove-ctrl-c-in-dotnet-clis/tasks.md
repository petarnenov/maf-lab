# Tasks

No Jev call is added or changed, so the Jev review checklist does not apply.

## 1. SIGINT tests

- [x] 1.1 In `CliCancelTests`, make `InterruptAsync` take the signal (`TERM` or `INT`); for `INT`, start the tool
      through a `python3` launcher that sets SIGINT to its default and `execvp`s `dotnet <tool> <args>`, and send
      `kill -INT` to the same pid. Verify the existing SIGTERM tests still pass unchanged in what they assert.
- [x] 1.2 Turn the A2AProbe, Eval and Indexing tests into theories over both signals, with the same assertions (exit
      130 and the tool's own "cancelled" last line). Verify all six pass.
- [x] 1.3 Correct the class comment: SIGTERM and SIGINT are both sent, and why SIGINT needs the launcher.
- [x] 1.4 Verify the SIGINT test catches a missing handler: remove `Console.CancelKeyPress` from
      `src/Maf.Lab.Eval/Program.cs`, see the Eval SIGINT test fail (and its SIGTERM test pass), then restore the line.
      Found: without the line, SIGINT ended Eval with exit 134 and no "cancelled" line, and its SIGTERM test still
      passed.

## 2. Checks

- [x] 2.1 Run `make lint` and `make test`, and verify both pass.

## 3. Documentation

- [x] 3.1 No document is made untrue (proposal, Documentation impact). Run `make docs` and verify it changes nothing,
      then `make docs-check` and verify it passes.
- [x] 3.2 Run `npx --yes @fission-ai/openspec@1.13.1 validate prove-ctrl-c-in-dotnet-clis --strict` and verify it
      passes.
