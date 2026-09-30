# coverage-ingestion Specification

## Purpose
Turns coverage reports from the .NET and web test toolchains into one stored, commit-keyed model that the Coverage
screen and the test-generation runs read from.

## Requirements

### Requirement: One normalised coverage model
The system SHALL ingest Cobertura XML reports produced by the .NET unit test run and by the Vitest run. It SHALL
normalise them into one model: file → lines (line number, hit count) → branches (covered, total) per line. Every file
path SHALL be stored relative to the repository root with forward slashes, whatever absolute or source-root-relative
form the report used. A path that does not resolve to a file inside the repository SHALL be dropped and counted, not
stored. Generated and test files SHALL NOT appear as coverage targets.

#### Scenario: .NET report with absolute paths
- **WHEN** a .NET Cobertura report lists `/src/src/Maf.Lab.Api/Coverage/CoverageStore.cs`
- **THEN** the file is stored as `src/Maf.Lab.Api/Coverage/CoverageStore.cs`

#### Scenario: Vitest report with source-relative paths
- **WHEN** a Vitest Cobertura report lists `coverage/CoveragePage.tsx` under source `web/src`
- **THEN** the file is stored as `web/src/coverage/CoveragePage.tsx`

#### Scenario: Path outside the repository
- **WHEN** a report names a file outside the repository root
- **THEN** that file is not stored and the ingestion result counts it as dropped

#### Scenario: Malformed report
- **WHEN** an uploaded report is not valid Cobertura
- **THEN** it is rejected as invalid with nothing stored

### Requirement: Snapshots keyed by commit
Each ingestion SHALL create a snapshot recording the commit SHA it was measured at, whether the working tree was
dirty, the time, the toolchain (`dotnet` or `vitest`) and its kind: `official`, or `candidate` for a run. For each
file, the dashboard SHALL use the most recent official snapshot that contains it. A candidate snapshot SHALL be linked
to its run and SHALL become official only when that run is accepted.

#### Scenario: Latest per file
- **WHEN** an older snapshot covers file A and B and a newer .NET snapshot covers only A
- **THEN** A comes from the newer snapshot and B from the older one

#### Scenario: Candidate does not replace official
- **WHEN** a verified run records a candidate snapshot for a file
- **THEN** the file's official coverage is unchanged until the run is accepted

### Requirement: Producing a snapshot on demand
An administrator SHALL be able to request a fresh snapshot at the current `main` commit. The request SHALL be run by
the coverage runner for both toolchains and SHALL be ingested as official. Only one refresh SHALL run at a time; a
second request while one is running SHALL be answered with the one already in progress. An administrator SHALL also
be able to upload a Cobertura report with its commit SHA and toolchain.

#### Scenario: Refresh
- **WHEN** an administrator requests Refresh coverage
- **THEN** a snapshot for each toolchain at `main`'s commit is ingested and the screen shows it

#### Scenario: Refresh already running
- **WHEN** a refresh is requested while one is running
- **THEN** no second refresh starts and the caller is told one is in progress

#### Scenario: Upload by a non-admin
- **WHEN** a user who is not a firm administrator uploads a report
- **THEN** the request is refused and nothing is stored

### Requirement: Coverage API
The api SHALL expose, to any signed-in user:

- the tree, with per-file coverage, the effective threshold, a candidate if present, and per-folder aggregates;
- the file detail, with source text at the snapshot's commit, per-line status, hit counts, branch counts and the
  summary;
- a file's snapshot history (time, commit, coverage, kind).

File detail SHALL be served only for paths present in a snapshot. It SHALL NOT read arbitrary paths. Error responses
SHALL NOT contain host paths or exception detail.

#### Scenario: Tree
- **WHEN** a signed-in user requests the tree
- **THEN** it returns every covered file with coverage and threshold, and every folder with its aggregate

#### Scenario: Traversal attempt
- **WHEN** file detail is requested for `../../etc/passwd`
- **THEN** it is answered as not found and nothing outside the repository is read

#### Scenario: History
- **WHEN** a file's history is requested
- **THEN** it lists its snapshots, newest first, with commit, time, coverage and kind

### Requirement: Refresh outcome names its reason
A refresh that does not succeed SHALL end with a summary that names the reason in user-facing words, without
internal detail. The reason SHALL be one of: interrupted (the service stopped while it ran), coverage runner
unavailable, no report produced by either toolchain, `main` has no commit, or failed for another reason. An
interrupted refresh SHALL be distinguishable from a failed one without reading server logs.

#### Scenario: Service stops mid-refresh
- **WHEN** the api is stopped while a refresh is running
- **THEN** the refresh job ends with the interrupted reason, not the generic failure

#### Scenario: Runner down
- **WHEN** a refresh is requested while the coverage runner cannot be reached
- **THEN** the refresh job ends failed with the reason coverage runner unavailable
