# Design

## Context

See proposal.md - Why. All three fixes are already on `main` (`6117c82`, `fbe4e59`, `dd50450`); this design records
the choices they made. The runner reads `dotnet test` results from console text: the per-test `failed <name>` lines
and the closing summary (`total / succeeded / failed / skipped`). The test agent and the api trust that result to
decide whether an attempt is green and whether a run may be verified and accepted.

## Goals / Non-Goals

**Goals:**
- A cut-off or summary-less test run can never be read as green.
- The output kept for a run stays bounded (400 000 characters), as before.
- The coverage tree tolerates any number of concurrent candidates.

**Non-Goals:**
- Making the runner's build match CI's lint. The runner builds without `-warnaserror`, while `make lint`/CI build
  with it, so a run can be verified and accepted with a file that breaks CI (CA2022 in this incident). Left as a
  follow-up: either build with the same analyzer settings in the runner or fail verification on new warnings in the
  changed files.
- Replacing console parsing with a structured result format (see Decisions).

## Decisions

**Keep head and tail under the same cap.** The output buffer keeps the first half of the cap from the start and a
rolling last half from the end, with a `... N line(s) of output omitted ...` marker between them.
- *Alternative: a larger cap.* Only moves the threshold; the test project keeps growing and the next overflow loses
  the summary again, silently.
- *Alternative: parse a TRX (or other logger) file.* The most robust source of counts, but it needs a logger
  argument, a file to collect from the workspace and a second parser per toolchain; a larger change than a bug fix
  warranted. The head+tail buffer plus the rule below closes the incident; TRX remains an option if console parsing
  proves fragile again.

**Distrust the absence of a summary.** Failed = max(summary's failed, number of named failed tests). If the build
succeeded and no summary was printed and nothing failed by name, the run counts one synthetic failure
`(test run)` "printed no summary". A false red costs one more attempt; a false green put failing tests on `main`.

**One candidate per file, from its own run.** The tree query takes, for each run in candidate state, only the
coverage row of the run's own file from that run's candidate snapshots, newest first. Each file has at most one
candidate run, so the tree's per-file key is unique again. Alternative: de-duplicate in the tree builder — rejected,
because it would pick an arbitrary run's whole-project measurement for files no run targeted.

**A document's synchronous flush is a no-op.** The translating stream buffers a document until it is complete, so a
flush has nothing to send; `FlushAsync` already did nothing for documents, and `Flush` now matches it instead of
calling the server's synchronous flush, which Kestrel refuses. Streaming answers still flush through.

## Risks / Trade-offs

- [The middle of a very long output is dropped] → only noise between the build and the summary is lost; named
  failures in that middle are lost too, but the summary's failed count still carries them.
- [A toolchain that never prints a summary would always be red] → only `dotnet` has the rule, and its summary is
  always printed by a run that finished; a timeout is reported separately.
- [The fix is not yet seen live] → the runner image must be rebuilt (`make`) before the running stack has it; see
  tasks.
