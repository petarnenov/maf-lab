# Tasks

## 1. Grade

- [x] 1.1 Make `GraphDepthSuite` take a `JevGrader` instead of `RubricJudge`, and grade each end-to-end answer with `GradeInput(question, answer, turn.Read, [])`. A failed grade scores the case 0 and counts as a judge failure. Fail a case at faithfulness < 0.75, relevance < 1 or no graph call. Remove `NeededContext`. Verify with `GraphDepthEvalTests` updated and passing
- [x] 1.2 In `RunGraphDepthAsync`, build the grader from the shared `JevClient` and refuse the end-to-end layer without the key (structural-only unaffected). Verify with a unit test of the refusal
- [x] 1.3 Delete `RubricJudge.cs` and `GenerationSuite.RubricPass`. Verify that `make lint-dotnet` and `make test` pass and that `git grep RubricJudge` finds nothing in src/ and tests/

## 2. Run

- [ ] 2.1 Run `make eval-graph-depth` once on the stack and verify it completes, reports the three variants with the Jev-graded scores, and counts no judge failures

## 3. Jev review

- [ ] 3.1 Run the Jev review checklist (docs/rules/jev-usage.md §7): the request is the existing grade request, unchanged (§79), now also used per graph-depth case; tested on labelled English and Bulgarian inputs through `generation-judge`. Record it in DECISIONS.md

## 4. Documentation

- [ ] 4.1 Update the graph-depth paragraph in README.md ("rubric scores" → the Jev grade), add a DECISIONS.md section, run `make docs` (never edit a `generated:` block by hand), then `make docs-check`
