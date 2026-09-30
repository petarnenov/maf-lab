# Tasks

## 1. Content roots

- [x] 1.1 Add `ProjectDir.Of(project)` to `tests/Maf.Lab.Tests` (walks up to `maf-lab.sln`, returns `src/<project>`,
      throws with the looked-for path when missing). Verify with a unit test that it finds `src/Maf.Lab.CoverageRunner`.
- [x] 1.2 Pass `["--contentRoot", ProjectDir.Of(...)]` to every `Program.BuildApp` call in the tests
      (`CoverageRunnerTests`, `TestGenRunsApiTests`, `TestAgentFactory`, `ComplianceAgentFactory`,
      `ReviewerSharedStateTests`). Verify by grepping: no `BuildApp([]` is left under `tests/`.
- [x] 1.3 Add the guard test: each in-process host resolves its own `A2A:Audience`. Verify it fails when the helper is
      removed from one host, then passes with it.

## 2. Verify

- [x] 2.1 Run `make test-dotnet` (the solution build that failed): no `CoverageRunnerTests` failure. Then build only
      the test project and run the tests again: the same result.
- [x] 2.2 Run `make lint-dotnet`, and `make docs-check` (no documentation is affected).
