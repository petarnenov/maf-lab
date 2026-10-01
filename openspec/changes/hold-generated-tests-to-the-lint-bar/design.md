# Design

## Context

See proposal.md - Why. The runner's `DotnetToolchain` runs `dotnet test --project tests/Maf.Lab.Tests -- --coverage …`,
which builds implicitly with the repository's settings (`TreatWarningsAsErrors=false` in `Directory.Build.props`).
Its console output names each warning once, as `path(line,col): warning CODE: message [project]`; promoted to an
error by `-p:TreatWarningsAsErrors=true` the same line reads `error CODE` and the build stops before any test runs
(both checked by hand with a CA2022 probe). `VitestToolchain` runs `vitest run --coverage`, which neither type-checks
nor lints. CI runs `dotnet build maf-lab.sln -warnaserror` and, in `web/`, `eslint . && prettier --check .`; the
ESLint config has no `--max-warnings`, so ESLint warnings pass CI.

Every caller goes through `JobExecutor`: the agent's baseline (no diff), each attempt and `run_tests` (the
workspace's diff), the api's verification (the report's diff, plus one run per suspected bug with that test
un-skipped) and the coverage refresh of `main` (no diff). The test-path allowlist means a diff only ever adds or
changes test files (`tests/Maf.Lab.Tests/**`, `web/src/**/*.test.ts(x)`).

## Goals / Non-Goals

**Goals:**
- A diff the runner reports green builds warning-free and lints clean in its own files, as CI requires.
- A lint breach reaches the agent as a diagnostic it can act on in the next attempt.
- A run without a diff measures exactly as before.

**Non-Goals:**
- The web type check (`tsc -b` in `make build-web`). Vitest strips types without checking them, so a test file
  with a type error can still pass the runner and fail CI's `build-web`. Same class of gap, but a whole-program
  check of a different step; left as a follow-up.
- Failing on warnings outside the diff's files, or on ESLint warnings: CI's bar for the diff, not stricter.
- Changing the runner's request or result shape.

## Decisions

**D1. dotnet: read the warnings, do not promote them.** The build keeps its default settings; the runner parses the
`warning CODE` lines it prints and, when the request has a diff, fails the build on each one whose file the diff adds
or changes. The diagnostic is the warning line itself (workspace prefix and project suffix removed).
- *Alternative: `-warnaserror` / `-p:TreatWarningsAsErrors=true` on diff-carrying runs.* Exactly CI's switch, but it
  fails on a warning anywhere in the build graph, including `src/`, and stops before the tests run. If `main` ever
  carried a warning, every attempt of every run would fail on a line the agent may not edit, and the attempt would
  return no test results or coverage to learn from.
- *Alternative: promote warnings only for the test project* (a conditional property in its `.csproj`). Narrower,
  but still fails the agent for warnings in test files it did not write, and still loses the test run.
- Warnings in the diff's files are precisely what the diff adds to CI's red, as long as `main` lints clean, which CI
  enforces on every push.

**D2. vitest: ESLint and Prettier on the diff's web files, after the tests.** `node_modules/.bin/eslint --format
json --output-file <out> --no-warn-ignored <files>` with the repository's `eslint.config.js`, then `prettier
--list-different --ignore-unknown <files>` with its `.prettierrc.json`/`.prettierignore`, both in `web/`. ESLint is
given only script files (`.ts .tsx .js .jsx .mjs .cjs`), Prettier every changed web file. Only severity-2 ESLint
messages fail. For each file Prettier lists, the runner runs `prettier --write` on the workspace copy (thrown away
with the workspace) and reports the first line that differs and how Prettier writes it, because the agent cannot run
Prettier itself and "not formatted" alone gives it nothing to fix. No package is added: `eslint` and `prettier` are
dev dependencies already in the image's `/opt/web/node_modules`; no network is needed.
- *Alternative: run the whole `npm run lint`.* Lints every file, so a stray finding in `main` would fail the diff,
  and it costs more; the diff's files are what can change.
- *Alternative: format the agent's files for it.* The diff is what gets merged and what the api re-measures; a
  runner that rewrote it would report on code nobody sent.

**D3. Only diff-carrying requests are checked.** The baseline and the refresh carry no diff and are untouched, so a
refresh of `main` always measures. The check needs no new request field: "has a diff" is the switch.

**D4. A lint failure is a build failure that keeps what was measured.** `Build` becomes `failed` with the lint
diagnostics appended; tests, failures and coverage stay. `Measured` and `Green` are false, so an attempt with a lint
finding is never the best clean attempt and a candidate with one is never verified. The next attempt's feedback
lists the lint diagnostics, the failing tests and the uncovered lines together.
- *Alternative: drop the coverage as for a compile error.* The tests did run; throwing their results away only
  costs the agent an attempt.

**D5. Fail closed.** If ESLint or Prettier cannot be started, exits with a configuration error, or writes no report,
the build fails with a `could not run` diagnostic. A check that did not happen does not pass.

**D6. One recognisable shape for lint diagnostics.** `path(line,col): warning CODE: …` (dotnet, as printed),
`path(line,col): eslint RULE: …` and `path(line): prettier: …`. `LintDiagnostics.Is` in `Maf.Lab.TestGen`
recognises them, so the agent can add its "these fail the build, as in CI" line and the api can tell a lint-only
failure from a compile error.

**D7. The api names lint, and a bug proof ignores it.** A verification build that failed with only lint
diagnostics ends `verification_failed` with "the tests do not pass lint". The suspected-bug proof runs the
candidate's diff with one test un-skipped; un-skipping can change formatting (`it.skip(` → `it(` may let Prettier
join a line), and that copy is never merged, so a lint-only failure there still counts as a run whose tests are
read.

**D8. The compiler-error pattern accepts mixed-case ids.** `error [A-Za-z]+\d+` so `error xUnit1031` (an analyzer
error) is named, not only counted as "Build failed".

## Risks / Trade-offs

- [A warning the diff causes in a file it does not touch] → not caught; with a test-only allowlist this is
  practically limited to project-wide warnings, which a test file does not produce. CI still catches it.
- [The `suspected-bug` marker is rewritten after measurement (issue link)] → the rewrite only lengthens a C# string or
  a TypeScript comment; neither produces a warning, and Prettier does not re-wrap comments.
- [Lint adds time to every diff-carrying vitest job] → two short node processes over a handful of files, a few
  seconds under the job's time limit; dotnet adds none (the warnings come from the build that already runs).
- [Console parsing of warnings] → the same output already parsed for errors; a format change would show up in the
  real-process test.

## Migration Plan

Rebuild the runner, test agent and api images (`make`) so the stack carries it. No data migration; rollback is a
revert.
