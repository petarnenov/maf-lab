# Proposal: focus test runs on the target

## Why

The test agent writes tests for one file, yet every build-and-test it asks for runs the whole unit suite. The runner
has no way to run fewer tests: `DotnetToolchain` runs `dotnet test --project tests/Maf.Lab.Tests -- --coverage …` and
`VitestToolchain` runs `vitest run --coverage …`, with no filter (`Toolchains.cs`), and `RunnerRequest` has no field to
ask for one. The target file is used only after the run, to read its line coverage from the report (`JobExecutor.cs`).

So every runner job of a run costs the whole suite: the baseline, each of up to 2 `run_tests` calls per attempt, each
attempt's measured run (up to 10 attempts), the api's proof of each suspected bug (up to 3), and the verification run.
That is up to 35 full runs for one run (1 + 10 × 3 + 3 + 1). Measured locally on 2026-10-01 (no build, coverage on),
the .NET suite (1147 tests) takes 317 s. Two classes that cover a target directly took 11–37 s. Vitest takes 42 s for the whole web suite
(599 tests), and `vitest related` on one module took 16–20 s. A whole run's output also grows with the suite. A recent
run printed more than the 400 000-character output cap, and the cut hid its summary (commit fbe4e59).

Some of the reasons for running everything are real:

1. **Coverage of the target comes from any test that runs it**, not only from its own test file. For example,
   `CoverageRefresher.cs` is 97.4 % covered by the whole suite, but no test file names it: the api tests reach it over
   HTTP. A run limited to the tests that name it measures 0 %.
2. **A new test can break another one**, through shared static state, a shared fixture or the file system. Only a run
   that includes the other tests shows that.
3. **The result must be green as a whole** before it becomes a candidate and can be merged into main.

Reason 3 applies to the verification run alone. Reason 2 applies to verification, and to the run that would end the
loop. Reason 1 applies to any number the run shows or compares, but the baseline already measures it once, and the
production code never changes during a run. None of the reasons requires the baseline, every attempt and every
`run_tests` call to run the whole suite.

## What Changes

- The runner accepts a test scope on each request: `all` (the default, as today) or `related`. With `related` (which
  needs a target file), it runs only:
  - the test files the diff adds or changes;
  - the existing tests that use the target or one of the changed test files.

  For .NET, "use" means a test file that names a type declared in that file, and the runner passes the test classes
  to Microsoft Testing Platform's `--filter-class`. For Vitest, it is the module graph: `vitest related --run`. When
  the diff touches something the rule cannot see through, the runner runs the whole suite and says why. Examples are
  a project file, a shared folder outside the unit test project, the Vitest setup file, or an empty selection.
- The runner's result says what ran (scope, the test files, and why it fell back). It also gives the target file's
  covered and uncovered lines, so a caller can combine them with an earlier run's lines.
- The test agent measures the baseline on the whole suite and keeps the target's line hits. Each attempt's measured
  run, and the model's `run_tests`, run the related tests only. Their target coverage is the union of the lines the
  baseline covered and the lines the focused run covered, so tests the run did not select still count.
- When a clean focused attempt reaches the target, the agent runs the whole suite on that diff before it stops. That
  run's numbers replace the attempt's. If another test fails there, the attempt is not clean: the failure goes to the
  next attempt as feedback, and the run does not end in `verification_failed`. The progress phase is `testing` while
  that run works.
- The api proves a suspected bug with the related tests only. Its verification run, and the coverage refresh, still
  run the whole suite.
- A run resumed from a checkpoint written before this change has no baseline line hits, so its attempts keep running
  the whole suite.

## Capabilities

### New Capabilities
- None.

### Modified Capabilities
- `coverage-runner`: a request may ask for the related tests only. The result says what ran and gives the target's
  line hits.
- `test-generation-agent`: attempts and `run_tests` run the related tests, with coverage merged with the baseline. The
  whole suite runs for the baseline and before the run stops at its target.
- `test-generation-runs`: a suspected bug's proof runs the related tests. The verification run stays the whole suite.

## Impact

- **Code.**
  - `Maf.Lab.TestGen`:
    - `RunnerRequest` gains `Tests`, and `RunnerResult` gains `Selection` and `TargetLines` (both optional and
      additive on the wire);
    - the merge of target line hits.
  - `Maf.Lab.CoverageRunner`:
    - the selection rule;
    - the toolchain arguments;
    - `IToolchainRunner` takes the selection.
  - `Maf.Lab.TestAgent`:
    - the baseline, the attempts and `run_tests`;
    - the confirming whole-suite run;
    - the checkpoint gains the baseline's line hits;
    - `run_tests` returns what ran.
  - `Maf.Lab.Api`: `RunVerifier`'s bug proof.
- **No new package, model or route.** The runner's HTTP route is unchanged, and the request and result only gain
  optional fields.
- **Progress.** There is no new CLI or UI action. The extra whole-suite run is shown as the attempt's `testing` phase in
  the run's status and activity, which already show the phase.
- **Jev.** No Jev call is added or changed.
- **Cost.** A typical run has 1 full baseline, focused attempts, at most one confirming full run per attempt that reaches
  the target, and 1 full verification. Today it has up to 35 full runs. The build of each job is unchanged.

## Documentation impact

- `DECISIONS.md`: a new section records the selection rule, the merge with the baseline and its known limit, the
  confirming run, and the measurements above.
- `README.md`, `CLAUDE.md`, `docs/*.md`, `openspec/project.md`, `.github/copilot-instructions.md`: none is made untrue.
  None of them describes which tests a runner job runs. `docs/http-api.md` covers the api's routes, not the runner's
  internal contract, and no api route changes.
