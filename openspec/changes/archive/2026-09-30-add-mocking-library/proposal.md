# Proposal

## Why

Run `r_2a1cf280` on `src/Maf.Lab.A2A/RedisPushConfigStore.cs` spent all 5 attempts ($0.34, about 539 000 tokens,
no budget) and wrote no test. The class takes an `IConnectionMultiplexer` and calls `IDatabase`. `tests/Maf.Lab.Tests`
has no way to stand in for these:
- there is no mocking library;
- there is no Redis container;
- there is no fake of `IDatabase`, and a hand-written fake of that interface runs to hundreds of members.

Each attempt, the model read the neighbouring code and weighed these same options until the tool-round cap ended it.
The same gap blocks `RedisTaskStore.cs` (also 0%) and any class behind a large interface. People writing tests by
hand hit it too.

## What Changes

- **NSubstitute 6.2.0 in `tests/Maf.Lab.Tests`.** It is pinned in `Directory.Packages.props` and recorded in
  `DECISIONS.md`. The coverage runner's image restores the test project's packages at build time, since it has no
  network at run time, so the new package reaches it with the next image build.
- **The agent is told it can substitute.** The dotnet instructions say that NSubstitute is available for interfaces
  (`Substitute.For<T>()`, `.Returns(...)`, `.Received()`), and that a large interface such as `IDatabase` is
  substituted, not implemented by hand.
- **The guardrail counts NSubstitute's checks as assertions.** A test that verifies its outcome only with
  `Received()` / `DidNotReceive()` (and their `WithAnyArgs` forms) is no longer reported as "asserts nothing".
- **One example test by hand.** `RedisPushConfigStore` gets a small test file that uses substitutes, so the library
  is exercised in CI and the agent has a pattern nearby to read.

Out of scope: Redis Testcontainers (a real Redis in integration tests), and any mocking for the `vitest` toolchain,
which already has `vi.fn()`.

## Capabilities

### New Capabilities
<!-- None. -->

### Modified Capabilities
- `test-generation-agent`: the test guardrails accept a substitute's received-call check as an assertion, and the
  agent's dotnet instructions name the substitution library as the way to stand in for interfaces.

## Impact

- `Directory.Packages.props` (`NSubstitute` 6.2.0), `tests/Maf.Lab.Tests/Maf.Lab.Tests.csproj` (reference).
- `src/Maf.Lab.TestGen/TestGuardrails.cs` (`IsAssertion`), `src/Maf.Lab.TestAgent/Instructions.cs` (dotnet rules).
- New `tests/Maf.Lab.Tests/RedisPushConfigStoreTests.cs`. It raises the file's coverage, which a later coverage
  refresh shows.
- The coverage-runner image must be rebuilt (`make` does this) so its seeded package cache holds NSubstitute.
- `DECISIONS.md`: the version table gains the row, and a new section records why NSubstitute (see design.md).

## Documentation impact

- `openspec/project.md`: the tech stack's test line gains "NSubstitute for substitutes in .NET unit tests". That
  text is the source of the generated context block in `openspec/config.yaml`, so `make docs` rewrites it.
- `DECISIONS.md` (as above), which is not in the docs-check set but is required when a package is added.
- README.md, CLAUDE.md, docs/*.md and .github/copilot-instructions.md do not list test libraries, so none changes.
