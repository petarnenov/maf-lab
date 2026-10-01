# Tasks

## 1. Contracts (Maf.Lab.TestGen)

- [ ] 1.1 `RunnerRequest.Fresh` (optional), `RunnerReuse(JobId, CompletedAt)` and `RunnerResult.ReusedFrom` (optional)
- [ ] 1.2 `AttemptRun` with optional `Run` and `Confirmation` on `AttemptActivity` and `AttemptLog`; `VerificationRun`
  with optional `TestGenReport.Verification`

## 2. Runner reuse (coverage-runner)

- [ ] 2.1 `Runner:ReuseResultsFor` (15 min) and `Runner:ReuseMaxResults` (16) in `RunnerOptions`
- [ ] 2.2 `JobQueue`: the key (full commit, toolchain, diff SHA-256, target, effective scope), the reuse store with
  window and bound, single-flight followers (reused on a reusable result, re-queued otherwise), and `fresh`
- [ ] 2.3 Reusable only when status `ok` and no failed test
- [ ] 2.4 `maf.runner.reuse` counter and a content-free log line per outcome
- [ ] 2.5 xUnit: hit; a miss on each key component; no reuse after a timeout or a failing test; single-flight with one
  run; a follower runs itself after a failed leader; expiry; bound; fresh; abbreviated commit; the existing queue test
  uses different requests

## 3. Agent attempt scope (test-generation-agent)

- [ ] 3.1 `TestGenerationHandler` fills `Run` and `Confirmation` on the attempt entry and the attempt log, including
  the agent's own reason when it asks for the whole suite
- [ ] 3.2 xUnit: a focused attempt confirmed on the whole suite carries both; an attempt the runner ran on the whole
  suite instead carries the reason and no confirmation

## 4. Api (test-generation-runs)

- [ ] 4.1 `RunVerifier` records `report.Verification` (scope, tests, pct, reusedFrom) and logs whether it reused;
  `CoverageRunner:ReuseForVerification` (default true) sends `fresh: true` when false
- [ ] 4.2 `RunActivityProjection` adds `run` and `confirmation` to `maf-lab/testgen-attempt`
- [ ] 4.3 xUnit: verification records a reused result; the bypass sends `fresh`; the event carries the fields and an
  older entry's event does not

## 5. Web (coverage-dashboard)

- [ ] 5.1 `runStream.ts` types; the attempt row in `RunActivity.tsx` shows the scope text
- [ ] 5.2 `CandidatePanel.tsx` names a reused verification run; `api/types.ts`
- [ ] 5.3 Vitest: related → confirmation; whole suite with a reason; reused marker; an old row without fields; candidate
  panel line

## 6. Verification

- [ ] 6.1 `make lint-dotnet`, `dotnet test --project tests/Maf.Lab.Tests`, `make test-web`, `make lint-web`,
  `make build-web`
- [ ] 6.2 Live check on a rebuilt stack: a run whose confirmation is followed by verification shows a `hit` in the runner log
  and `reusedFrom` in the report

## 7. Documentation

- [ ] 7.1 `docs/http-api.md`: the attempt event's `run` and `confirmation`, and `report.verification`
- [ ] 7.2 `docs/telemetry.md`: `maf.runner.reuse`
- [ ] 7.3 DECISIONS.md section for this change
- [ ] 7.4 Run `make docs` (no hand edits inside `generated:` blocks) and `make docs-check`
- [ ] 7.5 `openspec validate reuse-identical-runner-results --strict` and `openspec validate --specs --strict`
