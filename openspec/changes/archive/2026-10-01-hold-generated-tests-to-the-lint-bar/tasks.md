# Tasks

## 1. Lint diagnostics shared shape

- [x] 1.1 Add `LintDiagnostics` to `src/Maf.Lab.TestGen/RunnerContracts.cs` (format helpers for eslint and prettier,
  `Is`, `OnlyLint(RunnerResult)`); verify with a unit test that dotnet warnings, eslint and prettier lines are
  recognised and a compiler error is not

## 2. Coverage runner

- [x] 2.1 `Toolchains.cs`: `ToolchainOutcome` carries the warnings a build printed; `DotnetToolchain.Parse` fills
  them; the error pattern accepts mixed-case ids (`xUnit1031`); verify with parse tests
- [x] 2.2 `LintBar.cs`: changed files from the diff; dotnet warnings filtered to them; vitest ESLint (errors only)
  and Prettier (first differing line) on the changed web files; fail closed when the tools cannot run; verify with
  unit tests on the parsing and a real-process test against the repository's own ESLint/Prettier config
- [x] 2.3 `JobExecutor.cs`: apply the lint bar to diff-carrying requests only, keeping what was measured; verify that
  "a test that triggers CA2022" is a build failure with that diagnostic and coverage, "a warning outside the diff"
  and "no diff, warnings present" stay ok (FakeToolchain with warnings)

## 3. Test agent

- [x] 3.1 `Instructions.System` states the lint bar (warnings fail; ESLint and Prettier with the repository's
  settings); `Instructions.Feedback` adds the "fail the build, as in CI" line when lint diagnostics are present;
  `run_tests` carries the same note; verify with tests in `TestAgentRoundsTests`
- [x] 3.2 End to end through the agent: attempt 1's runner result carries a CA2022 build failure, attempt 2's input
  carries it and the note; verify with a test in `TestAgentTests`

## 4. Api verification

- [x] 4.1 `RunVerifier`: a lint-only build failure ends `verification_failed` with "the tests do not pass lint"; a
  suspected bug's proof run with only lint diagnostics is read by its tests; verify with tests in
  `RunVerificationTests`

## 5. Checks

- [x] 5.1 `make lint`, `dotnet test --project tests/Maf.Lab.Tests`, `make test-web` pass
- [ ] 5.2 Live check on a rebuilt stack. Steps: (1) after this change is on `main`, run `make` (rebuilds the
  coverage-runner, test-agent and api images and waits until healthy); (2) on http://localhost:7171 open Coverage,
  pick a small `src/` file with the dotnet toolchain and start a run; (3) in the run's activity, any attempt whose
  test file warns shows build `failed` with a `path(line,col): warning CA…` error and the next attempt's result
  without it, and a vitest run on a `web/src` file shows `eslint …`/`prettier: …` errors the same way; (4) the
  accepted branch passes `make lint`. To force the dotnet case, the runner tests' CA2022 line comes from a test
  calling `Stream.Read(buffer, 0, 1)` on a `Stream`-typed variable (a `MemoryStream`-typed one is exempt)

## 6. Documentation

- [x] 6.1 Add the DECISIONS.md section for this change (D1–D8 in short); the proposal's Documentation impact names
  no other document
- [x] 6.2 Run `make docs` (no `generated:` block edited by hand) and `make docs-check`; both pass
