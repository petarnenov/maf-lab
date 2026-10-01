# Tasks

## 1. Test results from a complete run (coverage-runner)

- [x] 1.1 Keep the first and last half of the 400 000-character output cap with an omitted-lines marker
  (`CappedOutput` in `src/Maf.Lab.CoverageRunner/ChildProcess.cs`, commit `fbe4e59`); verified by
  `CoverageRunnerTests.Output_over_the_cap_keeps_its_beginning_and_its_end`
- [x] 1.2 Count failed as max(summary, named failures) and one "printed no summary" failure when a built run has no
  summary (`DotnetToolchain.Parse`, commit `fbe4e59`); verified by
  `CoverageRunnerTests.A_dotnet_run_whose_summary_is_missing_is_not_green`
- [ ] 1.3 Live check on a rebuilt coverage runner: a full-project `dotnet` run that prints over the cap reports its real
  failed count. Not done: needs the stack rebuilt with `make`, which this retroactive change did not run

## 2. Several candidates at once (coverage-dashboard)

- [x] 2.1 Each candidate run contributes only its own file's newest candidate totals (`CoverageStore.CandidateTargetsAsync`,
  `CoverageEndpoints.TreeAsync`, commit `6117c82`); verified by
  `CoverageApiTests.Two_candidates_at_once_each_show_their_own_files_coverage`

## 3. Flushing a document (a2a-hosting)

- [x] 3.1 A synchronous `Flush` of a document answer is a no-op, as `FlushAsync` is (`SpecWireStream.Flush` in
  `src/Maf.Lab.A2A/SpecWireMiddleware.cs`, commit `dd50450`); verified by the `/a2a/probe` and `/a2a/flush` cases in
  `SpecWireMiddlewareTests`
- [x] 3.2 Correct the accepted SpecWireMiddleware tests: stand-ins for `/a2a/message:send` and `/a2a/tasks/t-1:cancel`,
  `ReadExactly` for CA2022 (commit `dd50450`); verified by `SpecWireMiddlewareTests` passing

## 4. Verification

- [x] 4.1 The `CoverageApiTests`, `CoverageRunnerTests` and `SpecWireMiddlewareTests` classes pass
  (`dotnet test --filter`) on `main` at `dd50450`

## 5. Documentation

- [x] 5.1 No document is made untrue or incomplete (see proposal, Documentation impact), so no document is edited
- [x] 5.2 Run `make docs-check`; it succeeds
