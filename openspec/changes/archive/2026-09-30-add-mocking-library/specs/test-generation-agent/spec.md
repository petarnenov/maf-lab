## MODIFIED Requirements

### Requirement: Test guardrails
The agent SHALL reject its own tests that are focused (`.only`, `fit`, `fdescribe`), that contain no assertion, or
that make a test pass by catching the exception it is meant to verify. A check on a substitute's received calls
(`Received`, `DidNotReceive`, `ReceivedWithAnyArgs`, `DidNotReceiveWithAnyArgs`) SHALL count as an assertion. A
generated test SHALL NOT modify production code, either in its source or at run time (for example by writing to,
moving or deleting a file under `src/` or `web/src/`). The agent SHALL NOT skip a test (`.skip`, `xit`,
`[Fact(Skip=...)]`, `[Theory(Skip=...)]`), except a suspected-bug skip that follows the requirement below. A
violating test SHALL count as a failed check in that attempt and SHALL be reported as feedback. The final diff SHALL
NOT contain a violation.

#### Scenario: Focused test
- **WHEN** a generated Vitest file contains `it.only(`
- **THEN** the attempt reports a guardrail violation, and the next attempt is told to remove it

#### Scenario: Assertion-free test
- **WHEN** a generated xUnit test calls the method under test and asserts nothing
- **THEN** it is reported as assertion-free, and the attempt does not count it

#### Scenario: A received-call check is an assertion
- **WHEN** a generated xUnit test calls the method under test and ends with `await db.Received(1).HashSetAsync(...)` on a substitute
- **THEN** no assertion-free violation is reported for it

#### Scenario: Setting up a substitute is not an assertion
- **WHEN** a generated xUnit test only configures a substitute with `.Returns(...)` and calls the method under test
- **THEN** it is reported as assertion-free

#### Scenario: Skip without a suspected bug
- **WHEN** a generated test is skipped with no suspected-bug marker, or is not listed as a suspected bug in the report
- **THEN** it is reported as a skipped test, and the attempt does not count it

#### Scenario: Test writes to production code
- **WHEN** a generated test writes to a file under `src/`
- **THEN** it is reported as modifying production code, and the attempt does not count it

## ADDED Requirements

### Requirement: Substitutes for interfaces
The `dotnet` test project SHALL provide a substitution library, and the coverage runner SHALL be able to build tests
that use it without network access. The agent's instructions for `dotnet` SHALL name that library as the way to
stand in for an interface the code under test depends on. They SHALL say that a large interface is substituted, not
implemented by hand.

#### Scenario: A class behind a large interface
- **WHEN** the agent works on a file whose class takes `StackExchange.Redis.IConnectionMultiplexer`
- **THEN** its instructions tell it to substitute the interface with the library, and a test that does so builds and runs in the coverage runner

#### Scenario: The runner has the package offline
- **WHEN** the coverage runner, which has no network, builds a diff that adds a test using `Substitute.For<IDatabase>()`
- **THEN** the build succeeds from the packages its image restored
