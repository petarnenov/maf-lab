# Design

## Context

See proposal.md (Why). The parts of the current setup that decide how this is done:
- **Package pinning.** Versions are pinned centrally in `Directory.Packages.props`, and every version move is
  recorded in `DECISIONS.md`.
- **The runner has no network.** Its image restores `tests/Maf.Lab.Tests` at build time into `/opt/nuget` (seeded
  from the shared BuildKit NuGet cache, DECISIONS §58). It builds each diff offline from that cache. So the package
  has to be referenced by the test project when the image is built; a model cannot add one later.
- **Writes are limited to `tests/`.** The agent may write under `tests/`, including the `.csproj`. A package it
  adds there would not be in the runner's cache, and the build would fail. The instructions keep it to what is
  already referenced.
- **Assertions are recognised by name.** `TestGuardrails.IsAssertion` accepts invocation names starting `Assert`,
  `Should`, `Verify`, `Expect`, or a call on `Assert`. NSubstitute verifies with `sub.Received(n).Method(...)`,
  whose invocation names are `Received` and `Method`, so today such a test is flagged "asserts nothing".

## Goals / Non-Goals

**Goals:**
- A generated or hand-written test can stand in for `IConnectionMultiplexer` / `IDatabase` in a few lines.
- The guardrail keeps its meaning: a test must check something. Configuring a substitute is not a check.

**Non-Goals:**
- A real Redis in tests (Testcontainers.Redis).
- Changing the agent's round cap, model or budget.
- Mocking for vitest.

## Decisions

### D1. NSubstitute, not Moq or FakeItEasy
NSubstitute 6.2.0 (the current release; 6.x targets .NET 8+).
- *Moq*: some versions shipped telemetry (SponsorLink) that scraped git e-mail at build time. That was withdrawn,
  but it is not something this repo wants in its supply chain, and its `Setup/Verify` lambdas are longer than
  NSubstitute's.
- *FakeItEasy*: capable, but less common, so a model is less likely to write it correctly on the first try.
- NSubstitute's syntax (`Substitute.For<T>()`, `x.Method().Returns(v)`, `x.Received().Method()`) is short and widely
  represented in training data. That matters when the writer is a model with a round cap.
- No Microsoft Agent Framework package covers test doubles, so the preference for `Microsoft.Agents.AI.*` does not
  apply here.

`NSubstitute.Analyzers.CSharp` is not added. It helps people, but it adds a second package to the runner cache for
no gain in the agent's loop.

### D2. The guardrail recognises received-call checks by name
`IsAssertion` also accepts invocation names `Received`, `DidNotReceive`, `ReceivedWithAnyArgs` and
`DidNotReceiveWithAnyArgs`, the same way it already accepts `Verify*` for Moq. `Returns`, `ReturnsForAnyArgs`,
`When/Do` and `Arg.*` are not assertions.
- *Alternative: resolve symbols semantically to NSubstitute's `SubstituteExtensions`.* The guardrail works on
  syntax only, with no compilation, and that is deliberate (it runs in the api and the agent without the test
  project's references). Rejected.

### D3. Tell the agent, briefly, in the dotnet rules
One bullet under "Only write test files … For dotnet:" says: NSubstitute is referenced; substitute interfaces with
`Substitute.For<T>()`; never hand-implement a large interface such as `IDatabase`; use only packages the test
project already references. It goes in the system instructions, not per attempt, so it costs no extra tokens per
round.

### D4. One hand-written example beside the file that failed
`RedisPushConfigStoreTests` covers save (hash set plus expiry on the task's key), list (deserialises entries and
skips bad ones), and delete. It uses substitutes for `IConnectionMultiplexer` and `IDatabase`. It proves the package
builds in CI and in the runner, and it is the nearby pattern the agent reads first. `RedisTaskStore` is left for the
agent, as the live check of the change.

## Risks / Trade-offs

- [A new test dependency grows the runner image and the restore] → The package is small (plus Castle.Core). The
  shared NuGet cache makes the rebuild cheap.
- [Name-based recognition accepts a helper called `Received…` that asserts nothing] → This is the same trade-off
  the rule already makes for `Assert*` / `Verify*` helpers. It is documented next to the rule.
- [The agent edits the test `.csproj` to add another package] → The instructions forbid it. If it happens, the
  runner build fails offline, and that failure is fed back to the next attempt.

## Migration Plan

1. Pin the package, reference it, and add `DECISIONS.md` §59 and the version row.
2. Change the guardrail and the instructions, with tests.
3. Add the example test.
4. `make` rebuilds the runner image. The live check is a run on `src/Maf.Lab.A2A/RedisTaskStore.cs`.

