# Proposal

## Why

`make test-dotnet` (`dotnet test --solution maf-lab.sln`) fails 9 `CoverageRunnerTests` with `401`. Four referenced
projects each ship an `appsettings.json` (Api, CoverageRunner, TestAgent, ComplianceAgent), and all four land at the
same path in `tests/Maf.Lab.Tests/bin/…`. The last one copied wins. After a solution build that is the test agent's
file, with `A2A:Audience = maf-lab-test-agent`. The in-process coverage runner (`Program.BuildApp([])`) uses the
working directory as its content root, so it reads that file and rejects the runner-audience tokens the tests
issue. A build of the test project alone happens to copy the runner's file last, so the failure depends on how the
tests were built. `make ci` and the CI dotnet job use the solution build.

## What Changes

- Every test that hosts a service in-process through its `Program.BuildApp` (coverage runner, test agent, compliance
  agent) passes that project's own directory as the content root. It then reads its own `appsettings.json`, as it
  does in its container, never whichever copy won in the test output.
- A guard test starts each in-process host and checks it has its own `A2A:Audience`. Build order can then no longer
  change which configuration a host gets.
- No production code changes, and no behavior changes. This is test infrastructure only (`skip_specs`).

## Capabilities

### New Capabilities

(none)

### Modified Capabilities

(none — no requirement changes; the change declares `skip_specs: true`)

## Impact

- `tests/Maf.Lab.Tests`: a small helper that resolves a project's source directory, and its use in
  `CoverageRunnerTests`, `TestGenRunsApiTests`, `TestAgentFactory`, `ComplianceAgentFactory` and
  `ReviewerSharedStateTests`, plus the guard test.
- `make test-dotnet` and the CI dotnet job become independent of build order.

## Documentation impact

None. No document describes how the tests pick a content root. `README.md`'s test section says what `make
test-dotnet` runs, and that stays true.
