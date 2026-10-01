## ADDED Requirements

### Requirement: Related tests only, on request
A request MAY name a test scope: `all` or `related`. Without one, the scope SHALL be `all`, and the runner SHALL run
the toolchain's whole unit suite as before. A `related` request without a target file SHALL be refused as invalid.

With `related`, the runner SHALL run only the related tests in the workspace after the diff is applied:

- every test file the diff adds or changes;
- every other test file that uses the target file or one of those changed test files.

For `dotnet`, a test file uses a file when it names a type that file declares. For `vitest`, a test file uses a file
when the file is in its import graph.

The runner SHALL run the whole suite instead, and SHALL say why, when either holds:

- the diff changes something the rule cannot follow: a file outside the toolchain's test sources, a project or package
  file, or the web test setup file;
- the rule selects no test.

Every result SHALL say what ran:

- the scope that was used;
- the test files of a `related` run;
- the reason when it fell back to `all`.

When a target file was named, the result SHALL also give that file's covered and uncovered line numbers, as measured
by this run.

#### Scenario: A new test file and the tests that use the target
- **WHEN** a `related` dotnet request adds `tests/Lab.Tests/CalcTests.cs`, and an existing `tests/Lab.Tests/UsesCalc.cs`
  names a type declared in the target file
- **THEN** the runner runs the classes of those two files only, and the result's scope is `related` with both files

#### Scenario: A change the rule cannot follow
- **WHEN** a `related` dotnet request changes the test project file
- **THEN** the runner runs the whole suite, and the result's scope is `all` with the reason

#### Scenario: Nothing selected
- **WHEN** a `related` request has no diff and no test file uses the target
- **THEN** the runner runs the whole suite, and the result says that nothing was selected

#### Scenario: Related without a target
- **WHEN** a request asks for `related` and names no target file
- **THEN** it is refused as invalid, and nothing runs

#### Scenario: Default scope
- **WHEN** a request names no scope
- **THEN** the whole suite runs, as before

#### Scenario: Line hits returned
- **WHEN** a request names a target file
- **THEN** the result lists the target's covered and uncovered line numbers from this run
