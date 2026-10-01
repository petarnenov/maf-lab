# Proposal

## Why

When an attempt is clean on its related tests and reaches the target, the test agent runs the whole suite on that
attempt's diff to confirm it. Once the task completes, the api's verification sends the coverage runner exactly the
same request: same commit, same diff, same target, whole suite. A whole .NET run takes about 75–80 s including the
clone and the build. A successful run therefore pays for the same job twice, back to back. The same happens on a
smaller scale when the model's last `run_tests` call and the attempt's measured run carry the same diff.

The attempt card on the Coverage screen also shows only the final counts (for example "1219 passed"). It does not say
that the attempt itself ran only 58 tests from 3 related files, or that the 1219 came from the whole-suite
confirmation. The related run's numbers appear only in the tool summary, so a reader cannot tell what an attempt
actually ran.

## What Changes

- The coverage runner keeps the complete results it computed for a short window. An identical request is answered
  from that result without running anything. A request is identical when it has the same full commit id, toolchain,
  diff (by SHA-256), target file and test scope. Only results the runner computed itself are kept, and only complete
  ones: status `ok` with no failed test. A failed run, a timeout, a rejected diff or a runner error is never reused.
- An identical request that arrives while the first is queued or running attaches to it and waits, instead of running
  a second copy. If the first result cannot be reused, the waiting job runs on its own.
- A reused result says so: `reusedFrom: { jobId, completedAt }` names the job that computed it. A request can ask for a
  fresh run (`fresh: true`), which skips the reuse. The api sends that for verification only when
  `CoverageRunner:ReuseForVerification` is `false`. The default is `true`.
- The runner caps what it keeps (`Runner:ReuseResultsFor`, default 15 minutes; `Runner:ReuseMaxResults`, default 16).
  It logs and counts every hit and miss (`maf.runner.reuse`, tagged `outcome`), with no request content.
- The api's verification records how its run was obtained in the stored report (`report.verification`: scope, test
  counts, and `reusedFrom` when it reused the agent's confirmation). The candidate panel shows this as one line.
- Each attempt's activity entry, and its line in the report, says what the attempt ran:
  - `run`: the scope, the number of related test files, the tests run, the fallback reason when the whole suite ran
    instead, and whether the result was reused;
  - `confirmation`: the whole-suite run's tests and coverage, when one ran.

  The `maf-lab/testgen-attempt` event carries both. The attempt card reads, for example,
  "related: 3 files, 58 tests → whole suite: 1219 tests (confirmation)" or "whole suite: nothing related was selected".
  Entries recorded before this change have neither field, and the card shows them as before.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `coverage-runner`: a new requirement that an identical request reuses a complete result, with single-flight, a
  bound, a bypass and visibility.
- `test-generation-runs`: "Which tests verification runs" allows the verification run to be a reused runner result
  and requires the report to record it. "Progress to the browser over SSE" makes the attempt event carry the attempt's
  scope.
- `test-generation-agent`: "Activity reporting" requires an `attempt` entry to carry what the attempt ran and its
  confirmation.
- `coverage-dashboard`: "Run activity view" requires the attempt row to show its scope.

## Impact

- `src/Maf.Lab.TestGen`: `RunnerContracts.cs` gains optional fields: `RunnerRequest.Fresh`, `RunnerResult.ReusedFrom`
  and `RunnerReuse`. `AgentContracts.cs` gains `AttemptRun`, optional `Run` and `Confirmation` on `AttemptActivity` and
  `AttemptLog`, and `VerificationRun` with optional `TestGenReport.Verification`.
- `src/Maf.Lab.CoverageRunner`: `JobQueue.cs` gets the reuse store and single-flight. `RunnerOptions.cs` gets two
  options. The `maf.runner.reuse` counter is added.
- `src/Maf.Lab.TestAgent/TestGenerationHandler.cs`: fills `Run` and `Confirmation`.
- `src/Maf.Lab.Api/Coverage`: `RunVerifier.cs` records `Verification`, `RunActivityProjection.cs` adds the fields to
  the event, and `CoverageRunnerOptions.cs` adds `ReuseForVerification`.
- `web/src/coverage`: `runStream.ts` and `RunActivity.tsx` (attempt row), `CandidatePanel.tsx` and `api/types.ts`.
- Tests in `tests/Maf.Lab.Tests` and `web/src/coverage`.

All wire changes are additive and optional, so an older agent or stored rows still read. No route, make target,
project, model, package or load-balancer location changes. No Jev call is added or changed. No new CLI tool or UI
action is added. A reused result returns at once, and the existing themed progress (phase, attempt n/N) is unchanged.

## Documentation impact

- `docs/http-api.md`: the `/api/coverage/runs/{id}/events` row names the attempt event's new `run` and
  `confirmation` fields. The `/api/coverage/runs/{id}` row names `report.verification`.
- `docs/telemetry.md`: the metrics table gains `maf.runner.reuse` and its `outcome` tag.
- README.md, CLAUDE.md, openspec/project.md and .github/copilot-instructions.md do not describe runner jobs, the
  attempt card or the verification run at this level, so they are not affected.
