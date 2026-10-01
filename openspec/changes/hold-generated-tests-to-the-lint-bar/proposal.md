# Proposal

## Why

The coverage runner builds model-written tests the way `dotnet test` and `vitest` do by default, where a warning is
only a warning and nothing checks formatting. CI does not: `make lint` builds the solution with `-warnaserror` and
runs ESLint and `prettier --check` over the web app. So the test agent can write, and the api can verify and accept,
a test file that then turns `main` red. It happened: commit `2b038ba` (tests for `src/Maf.Lab.A2A/SpecWireMiddleware.cs`,
run `r_f69a978c…`) was verified green and accepted, and `make lint` failed on it with CA2022 until `dd50450` fixed it
by hand. The archived change `trust-only-a-complete-test-run` left this as a follow-up.

## What Changes

- When a runner request carries a diff, the runner holds the files the diff adds or changes to the lint bar CI
  applies, and reports a breach as a build failure with one diagnostic per finding:
  - `dotnet`: every compiler or analyzer warning the build prints for one of those files (for example
    `warning CA2022`) fails the build, as `-warnaserror` would. Warnings in files the diff does not touch are not
    the diff's doing and do not fail it.
  - `vitest`: the changed web files are run through the repository's own ESLint config and Prettier config, as
    `npm run lint` does. An ESLint error, or a file Prettier would format differently, fails the build; the
    Prettier diagnostic names the first line that differs and how Prettier writes it. ESLint warnings do not fail,
    because they do not fail CI either. No package is added: both tools are already in the web app's dev
    dependencies, which the runner image installs.
  - A lint failure keeps what the run measured (test counts, failures, the target's coverage), so the next attempt
    sees everything at once; the run is still not green and cannot be verified.
  - If the lint tools cannot run, the build fails: a candidate is never green on a check that did not happen.
- A request without a diff (the agent's baseline, the coverage refresh of `main`) is built and measured exactly as
  before, so a warning that reached `main` some other way never stops a refresh from measuring.
- The runner's compiler-error pattern also matches analyzer ids that are not all upper case (`xUnit1031`), so such
  an error is named in the diagnostics instead of only "Build failed".
- The test agent's rules say that warnings, ESLint errors and Prettier formatting in its files fail the build as they
  fail CI, with the Prettier settings spelled out; an attempt's feedback and a `run_tests` result that carry lint
  diagnostics add one line saying so.
- The api's verification names a lint breach as such (`the tests do not pass lint`), and the run that proves a
  suspected bug (the candidate with one test un-skipped, never merged) is judged by its tests, not by lint.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `coverage-runner`: a new requirement, "Model-written changes are held to the lint bar CI applies".
- `test-generation-agent`: "Attempt loop with feedback" — lint diagnostics are fed back and the rules say warnings
  count as errors.
- `test-generation-runs`: "Independent verification" — the build must also meet the lint bar; the reason names it;
  a suspected bug's proof run is not failed by lint.

## Impact

- `src/Maf.Lab.CoverageRunner/`: `LintBar.cs` (new), a small hook in `JobExecutor.cs`, the warning and error patterns
  in `Toolchains.cs` (`ToolchainOutcome` gains the warnings a build printed).
- `src/Maf.Lab.TestGen/RunnerContracts.cs`: `LintDiagnostics` (how a lint diagnostic is written and recognised).
- `src/Maf.Lab.TestAgent/Instructions.cs`, `TestAgentTools.cs`: the rule, the feedback line, the `run_tests` note.
- `src/Maf.Lab.Api/Coverage/RunVerifier.cs`: the reason, and the proof run's judgement.
- Tests in `tests/Maf.Lab.Tests` (`CoverageRunnerTests`, `TestAgentRoundsTests`, `TestAgentTests`,
  `RunVerificationTests`).
- `DECISIONS.md`: a new section for the choices below.

No API route, make target, project, model, package or load-balancer location changes. The runner's request and
result shapes are unchanged. No Jev call is added or changed. No new CLI tool or UI action; a lint check adds a few
seconds to a runner job, which the existing run progress (phase `building`) already covers.

The running stack has the new behavior only after the runner, test agent and api images are rebuilt.

## Documentation impact

None of README.md, CLAUDE.md, docs/*.md, openspec/project.md or .github/copilot-instructions.md describes how the
coverage runner builds or what makes a candidate's build fail; README's `make lint` rows and the copilot
instructions describe CI's lint, which is unchanged. The choices are recorded in a new `DECISIONS.md` section.
