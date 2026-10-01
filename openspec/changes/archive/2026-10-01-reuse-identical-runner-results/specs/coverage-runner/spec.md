## ADDED Requirements

### Requirement: An identical request reuses a complete result
The runner SHALL answer a request from a result it computed earlier, without running anything, when all of these hold:

- the earlier request was identical: the same full 40-character commit id, the same toolchain, the same diff
  (compared by its SHA-256), the same target file and the same test scope (no scope is `all`);
- the earlier result is complete: status `ok` and no failed test. A failed build or a lint failure with every test
  passing is complete;
- it finished less than the reuse window ago (configurable, 15 minutes by default);
- the request does not ask for a fresh run.

A result that timed out, was rejected, failed to check out or restore, ended in a runner error, or has a failed test
SHALL never be reused. A request with an abbreviated commit id SHALL never be reused or stored for reuse. Results SHALL
only ever come from the runner's own runs: a caller cannot supply or change one.

A reused result SHALL be the earlier result unchanged, with what it was reused from: the id of the job that computed
it and when that job completed. A result the runner computed for the request SHALL carry neither.

While an identical request is queued or running, a new request SHALL wait for it instead of running a second copy, and
SHALL report that job's state and queue position. When that job's result can be reused, the waiting request SHALL get
it as a reused result. When it cannot, the waiting request SHALL be queued to run on its own. A request that asks for a fresh run SHALL neither wait for nor reuse another job, and its complete result SHALL replace
any kept one.

The runner SHALL keep at most a configured number of results for reuse (16 by default), dropping the oldest first, and
SHALL forget them on restart. For every request it SHALL log and count whether it was a hit, joined a running job, was
a miss, was a fresh run or could not be reused at all, with the toolchain and scope only and no request content.

#### Scenario: The same request twice
- **WHEN** a whole-suite request for a commit, a diff and a target completes green, and the same request comes 40
  seconds later
- **THEN** the second answer is that result, at once, naming the first job as the one it was reused from, and no test
  runs

#### Scenario: Any difference runs again
- **WHEN** a request differs from a completed one only in its diff, its target file, its scope, its toolchain or its
  commit
- **THEN** the runner runs it and the result is not marked reused

#### Scenario: A failure is not reused
- **WHEN** an identical request follows one that timed out, or one that had a failing test
- **THEN** the runner runs it again

#### Scenario: Two identical requests at once
- **WHEN** an identical request arrives while the first is still running
- **THEN** the runner runs the tests once, and both callers get the result, the second marked reused

#### Scenario: The waiting request runs when the first fails
- **WHEN** an identical request waits for one that then times out
- **THEN** the waiting request runs on its own

#### Scenario: The window has passed
- **WHEN** an identical request arrives after the reuse window
- **THEN** the runner runs it again

#### Scenario: A fresh run is asked for
- **WHEN** an identical request asks for a fresh run
- **THEN** the runner runs it, and the result is not marked reused

#### Scenario: Only so many results are kept
- **WHEN** more complete results have finished within the window than the runner keeps
- **THEN** the oldest is no longer reused
