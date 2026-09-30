# Tasks

No TypeSafe Jev call is added or changed, so the Jev review checklist does not apply.

## 1. Package

- [x] 1.1 Pin `NSubstitute` 6.2.0 in `Directory.Packages.props`, reference it from `tests/Maf.Lab.Tests/Maf.Lab.Tests.csproj`, and add the version row and a §59 "Substitutes in .NET tests" section to `DECISIONS.md` in the same commit. Verify: `dotnet build maf-lab.sln -warnaserror` succeeds

## 2. Guardrail and instructions

- [x] 2.1 `TestGuardrails.IsAssertion`: accept `Received`, `DidNotReceive`, `ReceivedWithAnyArgs`, `DidNotReceiveWithAnyArgs`; update its doc comment. Verify: `TestGenSafetyTests` gains a test where a `Received(1)` check alone passes, and one where `.Returns(...)` alone is still "asserts nothing"
- [x] 2.2 `Instructions.System` (dotnet rules): NSubstitute is available; substitute interfaces with `Substitute.For<T>()`; never hand-implement a large interface such as `IDatabase`; use only packages the test project already references. Verify: an `Instructions` test that the system text names `Substitute.For` and says not to add packages

## 3. Example

- [x] 3.1 Add `tests/Maf.Lab.Tests/RedisPushConfigStoreTests.cs`, using substitutes for `IConnectionMultiplexer` / `IDatabase`, covering save (hash set and expiry on `push:<keyspace>:<taskId>`), list (deserialises entries, drops ones that do not parse to a record) and delete. Verify: the tests pass, and `make test-dotnet` reports `RedisPushConfigStore.cs` well above 0%

## 4. Verification

- [x] 4.1 `make lint` and `make test` pass — Done: lint clean; 1063 of 1064 pass. The one failure is `GuardrailTests.A_hanging_Jev_costs_no_more_than_the_timeouts`, a timing test that fails only under the parallel `make test` load and passes alone (seen before this change too)
- [x] 4.2 `make` rebuilds the runner image. Then, on http://localhost:7171, start a run with a small budget (e.g. $0.10) on `src/Maf.Lab.A2A/RedisTaskStore.cs`. Verify: the agent writes a test that uses `Substitute.For`, the runner builds it offline, and the coverage moves off 0% — Done: the runner image holds NSubstitute 6.2.0 and built RedisPushConfigStoreTests offline in a coverage refresh (17/17 lines). A $0.10 run on RedisTaskStore wrote a Substitute-based test in attempt 1 but ran out of budget. An unlimited run (glm-5.3, 12 tool rounds per attempt) wrote tests in attempt 3, fixed their compile errors in attempt 4, reached 100% and passed verification as a candidate ($0.34)
- [x] 4.3 `openspec validate add-mocking-library --strict` passes

## 5. Documentation

- [x] 5.1 `openspec/project.md`: add NSubstitute to the tech stack's test line. Verify: the line reads correctly
- [x] 5.2 Run `make docs` (it rewrites the generated context block in `openspec/config.yaml`), then `make docs-check`. Verify: `make docs-check` exits 0
