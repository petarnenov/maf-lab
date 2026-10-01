# Tasks

## 1. Wire contracts (Maf.Lab.TestGen)

- [ ] 1.1 Add `TestScope` (`all`, `related`) and `RunnerRequest.Tests` (optional, default `all`). Add
      `TestSelection(Scope, TestFiles, Reason)` and `LineHits(Covered, Uncovered)`, and add `RunnerResult.Selection`
      and `RunnerResult.TargetLines` as optional trailing fields. Verify with a JSON round-trip test: a request
      without `tests`, and a result without the new fields, still deserialise.
- [ ] 1.2 Move the uncovered-range rule from `TargetCoverage.Ranges` into `TestGen`, and add
      `FocusedCoverage.Apply(result, baselineLines)` (design D5). Verify with unit tests:
      - the 10-line scenario of the spec;
      - a target missing from the focused report;
      - a non-`related` result left unchanged;
      - the dashboard's rounding.

## 2. Coverage runner

- [ ] 2.1 Add the .NET selection rule (design D2, D4): seeds, declared type names, identifier-token matches, metadata
      class names, and the fallbacks. Verify with tests over a `TempGitRepo` fixture:
      - a new test file plus a user of the target are selected;
      - a nested and a generic class are named correctly;
      - a mention in a comment or a string does not select;
      - a changed `.csproj`, a file outside the project, and an empty selection fall back with a reason.
- [ ] 2.2 Add the Vitest selection (design D3, D4): related file arguments relative to `web/`, and fallbacks for a path
      outside `web/src/` and for the setup file (`Runner:VitestSetupFile`). Verify with unit tests of the arguments and
      the fallbacks.
- [ ] 2.3 Give `IToolchainRunner.RunAsync` the selection. `DotnetToolchain` adds one `--filter-class` per class;
      `VitestToolchain` runs `related --run --passWithNoTests <files>` and reports the test files from its JSON.
      Verify with argument tests for both toolchains, and with the Vitest parse test reading test files.
- [ ] 2.4 In `JobExecutor`:
      - plan the selection after the diff applies;
      - rerun `all` when a `related` run ran no test;
      - return `Selection` and `TargetLines`;
      - log only the scope and counts.

      In `Program`, validate `tests` (unknown → 400; `related` without a target → 400). Verify with
      `CoverageRunnerTests`:
      - through the real HTTP host, the fake toolchain receives the selection;
      - the result carries the scope, the files and the line hits;
      - the default is `all`;
      - malformed scopes are refused.

## 3. Test agent

- [ ] 3.1 Keep the baseline's `TargetLines`, store them in `RunCheckpoint.BaselineLines`, and hand them to the tools.
      Run each attempt's measured run as `related` when baseline lines exist, and merge it with
      `FocusedCoverage.Apply`. Verify with `TestAgentTests`:
      - the baseline asks for `all`;
      - attempts ask for `related`;
      - the merged coverage is fed back and reported.
- [ ] 3.2 Confirm a clean focused attempt that reaches the target with an `all` run of the same diff, in the `testing`
      phase, and judge the attempt on that run (design D7). Verify with tests:
      - a green whole run stops at the target with its measured coverage;
      - a failing whole run keeps the loop going and feeds the failure to the next attempt;
      - the progress shows `testing`.
- [ ] 3.3 `run_tests`: ask for `related` (or `all` without baseline lines), merge the coverage, and return `Scope` and
      `TestFiles` in `TestRun`. The activity summary names the scope and the file count. Add the instruction sentence
      (design D9). Verify with tool and summary tests.
- [ ] 3.4 Resume from a checkpoint without `BaselineLines` runs `all`. Verify with a `TestAgentRecoveryTests` case.

## 4. Api

- [ ] 4.1 `RunVerifier`: run each suspected bug's proof as `related` with the run's file as the target. Keep the
      verification run and `CoverageRefresher` on `all`. Verify with `RunVerificationTests` asserting each request's
      scope.

## 5. Checks

- [ ] 5.1 Confirm that no Jev call is added or changed, so the Jev review checklist (docs/rules/jev-usage.md §7) does
      not apply, and that no log line added carries message content: only scope, counts and durations. Verify by a
      grep of the diff for `Jev` and for the new log templates.
- [ ] 5.2 Run `make lint`, `dotnet test --project tests/Maf.Lab.Tests` and `make test-web`, and verify that all pass.
- [ ] 5.3 Live check on a rebuilt stack (not run by this change's author; it needs a rebuilt stack):
      1. Run `make` to rebuild the coverage runner and the agent.
      2. Start a run on a .NET file from the Coverage screen.
      3. In Jaeger, check that the attempts' `runner.run` spans are much shorter than the baseline's.
      4. Check that the activity's `run_tests` summary says "related".
      5. Check that the run reaching its target shows the `testing` phase.
      6. Check that verification passes and the candidate's coverage equals the confirming run's.

## 6. Documentation

- [ ] 6.1 Add a DECISIONS.md section:
      - the selection rule and its fallbacks;
      - the union with the baseline and its over-reporting limit;
      - the confirming run;
      - the measurements in the proposal;
      - no package or model moved.

      Verify by reading it against the code.
- [ ] 6.2 Check `README.md`, `CLAUDE.md`, `docs/*.md`, `openspec/project.md` and `.github/copilot-instructions.md`
      for sentences about which tests a runner job runs, and fix any that became untrue. Never edit inside a
      `generated:` block. Verify with a grep for `run_tests`, `coverage runner` and `whole suite`.
- [ ] 6.3 Run `make docs`, then `make docs-check`, and verify that it passes.
