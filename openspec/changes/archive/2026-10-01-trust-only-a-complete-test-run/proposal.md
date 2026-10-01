# Proposal

## Why

Run `r_f69a978c4c0348eead9975a7144a2f9e` (tests for `src/Maf.Lab.A2A/SpecWireMiddleware.cs`) was verified and
accepted into `main` with 4 failing tests. The coverage runner kept only the first 400 000 characters of a child
process's output; the full .NET test project now prints more than that, so the summary at the end (`failed: N`) was
cut off, the counts parsed as 0/0/0, and attempt 3 — build ok, with errors naming 5 failed tests — read as green.
The same morning, with two runs in candidate state at once, the Coverage page showed "Could not load coverage":
each candidate snapshot measures the whole project, so a file appeared once per candidate run and building the tree
failed on the duplicate. The accepted tests also exposed a real defect: a synchronous flush of a non-streaming A2A
answer was passed to Kestrel, which refuses synchronous writes.

All three were fixed directly on `main` without a change proposal. This change records them in the specs so the
behavior is a contract, not an accident of the current code.

## What Changes

- The coverage runner keeps the beginning and the end of an over-long output (first half and last half of the cap,
  with a marker naming how many lines were omitted), so the test summary at the end is never the part that is lost.
- A `dotnet` test result counts at least as many failures as the failed tests the output names, whatever the summary
  says; a run that built and printed no summary at all counts one failure ("printed no summary") and is never green.
- The coverage tree loads with any number of runs in candidate state at once; each candidate run contributes only
  its own file's candidate coverage (its newest candidate snapshot when it has several).
- A synchronous flush of a non-streaming (document) answer on the A2A surface is accepted as a no-op, as an
  asynchronous one already was.
- The SpecWireMiddleware tests the run produced are corrected (stand-in endpoints for the HTTP+JSON send and cancel,
  `ReadExactly` for CA2022). Test-only; no spec change.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `coverage-runner`: a new requirement that a test result is read only from a complete run (output cap keeps the
  end; missing summary or named failures are never green).
- `coverage-dashboard`: "Source tree with coverage" gains the several-candidates-at-once case.
- `a2a-hosting`: "The wire format is the specification's, not the SDK's" gains that flushing a document answer,
  synchronously or not, is accepted without failing or ending the response.

## Impact

Already committed on `main`:

- `6117c82` — `src/Maf.Lab.Api/Coverage/CoverageStore.cs` (`CandidateTargetsAsync`),
  `src/Maf.Lab.Api/Endpoints/CoverageEndpoints.cs` (`TreeAsync`), test in `tests/Maf.Lab.Tests/CoverageApiTests.cs`.
- `fbe4e59` — `src/Maf.Lab.CoverageRunner/ChildProcess.cs` (`CappedOutput`),
  `src/Maf.Lab.CoverageRunner/Toolchains.cs` (`DotnetToolchain.Parse`), tests in
  `tests/Maf.Lab.Tests/CoverageRunnerTests.cs`.
- `dd50450` — `src/Maf.Lab.A2A/SpecWireMiddleware.cs` (`Flush`), `tests/Maf.Lab.Tests/SpecWireMiddlewareTests.cs`.

No API shape, route, make target, project, model or load-balancer location changes. No Jev call is added or changed.
No new CLI tool or UI action; the existing progress feedback is unchanged.

## Documentation impact

None. No document states the runner's output cap or how a test summary is read; `docs/http-api.md` describes the
`/api/coverage/tree` shape (one `candidate` per file), which the fix makes true again rather than changes; the
SpecWire paragraph in `docs/http-api.md` and the divergence list in `DECISIONS.md` say nothing about flushing.
README.md, CLAUDE.md, openspec/project.md and .github/copilot-instructions.md do not describe any of this behavior.
