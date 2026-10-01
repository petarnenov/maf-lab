# Design: focus test runs on the target

## Context

See proposal.md, Why, for the evidence. The current shape:

- **Runner.**
  - `JobExecutor.RunAsync` clones the commit and applies the diff. It then calls
    `IToolchainRunner.RunAsync(workspace, outputDir, timeLimit, ct)` and reads the target's line coverage from the
    Cobertura report (`TargetCoverage.Of`).
  - `DotnetToolchain` runs `dotnet test --project <DotnetTestProject> -- --coverage …`.
  - `VitestToolchain` runs `vitest run --coverage … --reporter=json`.
  - `RunnerRequest(Commit, Toolchain, Diff?, TargetFile?)` has no way to choose tests.
- **Callers.**
  - The agent: the baseline (`TestGenerationHandler`, no diff), each attempt's measured run, and `run_tests`
    (`TestAgentTools`).
  - The api: each suspected bug's proof and the verification run (`RunVerifier`), and the refresh
    (`CoverageRefresher`, no target).
- **Test layout.** The .NET unit tests are one flat namespace, `Maf.Lab.Tests`, and their files are named after
  features, not source files. `RunVerifier.cs` is tested by `RunVerificationTests`, and `CoverageRefresher.cs` by
  `CoverageApiTests` over HTTP. A naming convention cannot find a target's tests.
- **The coverage tools.** xunit.v3 on Microsoft Testing Platform takes `--filter-class <FQN>`, which can be repeated;
  verified locally. Microsoft.Testing.Extensions.CodeCoverage reports every file of every loaded `src/` assembly, with
  0 hits for lines no selected test ran. It has no per-test attribution. Vitest's `include` makes every `src/` file
  appear in the report, and `vitest related --run <files…>` runs the tests whose import graph contains any of the files
  (a test file passed in counts as related to itself; verified).
- **Roslyn is already there.** `Maf.Lab.TestGen` references `Microsoft.CodeAnalysis.CSharp` for the guardrails, and
  the runner references `Maf.Lab.TestGen`.

## Goals / Non-Goals

**Goals:**
- Attempts and `run_tests` run seconds of tests, not minutes. The baseline, the run that would end the loop, and
  verification still run the whole suite.
- The coverage an attempt reports does not drop because a focused run skipped the tests that cover the target
  indirectly.
- A test that breaks another test is still caught before a candidate exists, and if possible while the run can still
  fix it.

**Non-Goals:**
- Making the build faster: each job still builds the whole test project in a fresh clone.
- Per-test coverage, or an index of which test covers which line.
- Narrowing the coverage refresh or the verification run.

## Decisions

### D1. The runner selects; the caller only names the scope
`RunnerRequest` gains `Tests` (`"all"` | `"related"`, default `all`). The runner computes the selection in the job's
workspace, after the diff is applied, because only there do the changed files and their content exist. The callers
then need no knowledge of the repository's test layout, and there is one rule for everyone. `related` without a target
is refused (`400`), like a malformed target.

Alternative: the agent sends a list of test classes. It would need its own parser and the same rule in the api for the
proof, and the runner would have to trust a list it could compute itself.

### D2. .NET: tests that name the target's types, plus the changed tests
The seeds are the target file and every changed `.cs` file under the unit test project. For each seed, Roslyn's syntax
tree gives the type names it declares. A test file of the project is selected when it is changed, or when an
identifier token in it is one of those names. Identifier tokens skip comments and string literals.

The runner then collects every class, record and struct declared in the selected files. It names each by its metadata
name: the namespace, then `+` for a nested type, and `` `N `` for generic arity. Each name is passed as
`--filter-class`. A helper type that holds no tests matches nothing and costs nothing.

Why types and one hop: it is a cheap static rule that finds the direct users. It also finds the users of a changed
helper, because the helper is a seed. Indirect users, such as the api tests that reach `CoverageRefresher` over HTTP,
are not found, and D5 is what keeps their coverage.

Alternatives:
- File names: they do not match in this repository.
- Per-test coverage: it would need one run per test.
- Traits: they would need annotating more than 100 files, and model-written tests would have to remember them.
- A call graph: it would need a semantic model, that is a compilation, in the runner.

### D3. Vitest: the module graph
The runner runs `vitest related --run --passWithNoTests <target> <changed web files…>`, with the same coverage and
JSON options, and paths relative to `web/`. Vitest's own graph is exact for static imports, so no rule of ours is
needed. The test files that ran are read from the JSON report.

### D4. Fall back to the whole suite, and say so
The runner runs `all` and gives a reason when:

- a changed path is not a source the rule follows. For dotnet, that is anything other than a `.cs` file under the unit
  test project (a project file, a fixture, `tests/Shared`). For vitest, that is a path outside `web/src/`, or the setup
  file (`Runner:VitestSetupFile`, default `web/src/test/setup.ts`), which every test loads but no test imports;
- the rule selects nothing (dotnet: no class), or the related run ran no test. The runner then runs the whole suite in
  the same job.

The result carries `Selection(Scope, TestFiles, Reason)`, with `TestFiles` capped at 50.

### D5. Coverage is the union with the baseline
The result gains `TargetLines(Covered, Uncovered)`: the target's executable line numbers from this run, split by hits.
The baseline runs `all` and keeps its `TargetLines`.

For a `related` result, the agent computes the following, with `TestGen`'s `FocusedCoverage.Apply`:

- executable = baseline covered ∪ baseline uncovered;
- covered = baseline covered ∪ (this run's covered ∩ executable);
- the percentage, with the rounding the dashboard uses (`FileCoverage.Pct`);
- the uncovered ranges, with the existing range rule, moved from the runner to `TestGen`.

If the target is missing from a focused report, because no selected test loaded its assembly, the baseline lines stand
as they are.

This is correct because the production code is fixed for the whole run, and the tests the focused run skipped are
unchanged since the baseline (every changed test file is selected). So they cover exactly what they covered then.

The merge is done in the agent, not the runner. The runner reports only what it measured, and the agent is what holds
the baseline (and its checkpoint).

### D6. The baseline stays whole
The baseline is one run per run. It is the truth that D5 merges into, its uncovered ranges are the first feedback, and
the report shows it as "baseline".

### D7. The run that would end the loop runs the whole suite
A focused attempt that is clean (green, no violation, a non-empty diff) and reaches the target triggers a second job
with the same diff and `all`. The progress phase is `testing` (`AttemptPhase.Testing`, defined and unused until now;
the status line and the activity already print the phase). Its result replaces the attempt's result before the
attempt is logged and judged. So:

- the coverage is measured, not merged;
- a test it breaks elsewhere makes the attempt unclean, and its failure is the next attempt's feedback;
- if the measured coverage turns out lower than the merge said, the loop simply goes on.

Why not on every attempt: that is today's cost. Why not leave it to the api: a failure there ends the run
`verification_failed` after all its attempts were paid for, with no chance to fix it.

The run can also end with its best clean attempt when it runs out of attempts or budget. That attempt was judged on a
focused run only, and the api's whole-suite verification is its check (D8).

### D8. The api: a focused proof, a whole verification
A suspected bug's proof only needs its un-skipped test to fail. It runs `related` with the run's file as the target.
The bug's test file is changed by the diff, so it is selected. The verification run stays `all`, and so does the
refresh. They are the regression check and the source of the candidate's and the dashboard's numbers.

### D9. The model sees what ran
`run_tests` returns `TestRun` with `Scope` and `TestFiles` (at most 20), and its coverage is the merged figure. The
system instructions gain one sentence: `run_tests` runs the changed tests and the tests that use the target, and the
whole suite runs before the run ends. The activity's one-line summary of `run_tests` names the scope and the number of
files.

### D10. Checkpoints
`RunCheckpoint` gains `BaselineLines` (optional). A checkpoint without it, written before this change, resumes with
the whole suite for its attempts. That is how it would have run before, so a resumed run never mixes merged and
unmerged numbers.

## Risks / Trade-offs

- **Over-reporting when an attempt removes coverage.** The union keeps the baseline's lines even if the attempt
  rewrote or deleted the only existing test that covered them. D7 and the verification measure the truth. Until then,
  the merged number, and which focused attempt counts as best, can be too high. Writing over existing tests is not
  what the agent is asked to do, so this is accepted and named in DECISIONS.
- **Breakage outside the selection.** A new test that breaks an unselected test is seen by D7 when the attempt reaches
  the target, and by verification in every case. A run that ends below its target can still fail verification for
  that reason, as it can today.
- **Over-selection.** Common type names (`Program`, `Options`) select many files. It costs time only, and never more
  than the whole suite.
- **The build is not focused.** Each job still builds everything, which is about 30 s warm on this machine and more in a
  fresh clone. The saving is in test execution: 317 s → 11–37 s for .NET, 42 s → 16–20 s for Vitest.
- **Two jobs on the last attempt.** The attempt that reaches the target pays for a focused run and a whole run. That is
  still one whole run fewer than today for that attempt, if it used `run_tests` once.
