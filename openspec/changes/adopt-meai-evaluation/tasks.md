# Tasks

## 1. Packages

- [x] 1.1 Add `PackageVersion` entries for `Microsoft.Extensions.AI.Evaluation` and `.Reporting` 10.10.0 to `Directory.Packages.props` and reference them from `src/Maf.Lab.Eval/Maf.Lab.Eval.csproj`. Add a DECISIONS.md section in the same commit (why Jev grades and the library only reports, versions, the D1 rejections). Verify with `dotnet build src/Maf.Lab.Eval`
- [x] 1.2 Confirm two things at 10.10.0: that `DiskBasedReportingConfiguration` accepts a null `ChatConfiguration` with caching off, and that the HTML writer is callable in-process (design D5). Record the answer in the DECISIONS.md section; if the writer is not callable, add `.Console` as a local tool instead

## 2. Datasets

- [x] 2.1 Add `referencePoints` to every row of `evals/generation.jsonl`: one fact, step or condition each, split by hand from `referenceAnswer`. Make the loader fail on a row without points, naming it. Verify with a loader unit test over the real file
- [x] 2.2 Write `evals/generation-judge.jsonl`: 16 hand-labelled rows (8 billing, 8 codebase, half Bulgarian) from recorded `generation` answers, each with points labelled stated or contradicted, including removed, paraphrased and contradicted points, with no client data. Add the loader and verify it with a loader unit test

## 3. The grade

- [x] 3.1 Implement `AnswerSentences.Split` (design D2). Verify with unit tests on English, Bulgarian and Latin-script Bulgarian prose, markdown lists, tables, back-ticked paths with dots and line ranges, fenced code, and decimals, using answers from `answer-check.jsonl`
- [x] 3.2 Build the Jev request (design D3): the state from the question, the sentences, the sources via `AnswerSources.Select` and the points. Generate the questions per sentence, point and source, plus `answer_relevant`, choosing the billing or codebase context as production does. Apply `MaxSentences` truncation. Verify with unit tests on the serialised request (field names, one question per item, no question asking to count, size under the cap at 60 sentences)
- [x] 3.3 Compute the metrics from the answers in code (design D4), with the lists behind them. Verify with unit tests on canned `FakeJev` answers: all high, one 0.1 sentence, a 0.45 sentence in the band, a no-claim answer, a contradicted point, and no sources
- [x] 3.4 Fallback and logging. Without a key, the suite refuses to start. A failed request scores the case 0 on every judged metric with `judge failed: <reason>`. Each request logs model, usage, latency, question count and case id only, and `judgeInputTokens` is summed into the run's settings. Verify with `FakeJev` failure tests, and by grepping a run's console and OTel output for a dataset sentence, which must be absent
- [x] 3.5 Wrap it all as `JevGenerationEvaluator : IEvaluator` (design D5): one `NumericMetric` per metric, the reason and lists as diagnostics, and the probabilities as metadata. Verify with a unit test over a canned `EvaluationResult`

## 4. Suites and report

- [x] 4.1 Run `GenerationSuite` through the evaluator via `ScenarioRun`s, storing under `evals/reports/meai/` (check with `git check-ignore`). Extend `JudgeScore`, keep `JevMetrics` on faithfulness and relevance, and put the new scores in the per-case progress line. Update `AnswerCheckEvalTests` and `EvalHarnessTests`, and verify that `make test` passes
- [x] 4.2 Add the `generation-judge` suite (design D6): accuracy overall and per domain, language and split; the production check's accuracy on the same rows beside it; failures named with their probabilities; a progress line per case over the known count; refusal without the key. Add it to the `all` list and to the `eval` target's help text. Verify with a `FakeJev` suite test and one live run
- [x] 4.3 Write `evals/reports/<runId>.html` after `generation` and `generation-judge` runs. Verify that the file lists every case with its scores and unsupported sentences

## 5. Side-by-side comparison

- [x] 5.1 Make `generation` also score each turn with `RubricJudge` and report `rubric:*` and `judgeAgreement:*`, with no thresholds. Verify with a unit test of the agreement computation and one live run
- [x] 5.2 Run `make eval-generation` five times and `make eval SUITE=generation-judge` twice. Collect the per-run metrics, the run-to-run ranges for both judges, judge failures, every rubric/Jev disagreement with both reasons, and whether the two `generation-judge` runs are identical
- [x] 5.3 Apply the gate (design, Migration Plan step 3) and write the outcome into DECISIONS.md: the numbers, each disagreement and which judge was right, g-01 and g-04 named, and the Bulgarian accuracy. If the gate fails, stop here, keep `RubricJudge`, and revise this change with the user before group 6

## 6. Switch

- [ ] 6.1 Remove `RubricJudge`, its `Parse` test, and the `rubric:*` and `judgeAgreement:*` metrics. Verify with `make test` and `make lint`
- [ ] 6.2 Re-measure the `generation` tolerances from the five runs. Replace the 0.035 judge-noise override in `Evals:RegressionTolerances` with what was measured, recording the source. Add thresholds for `completeness`, `referenceAgreement`, `retrievalJudged` and `generation-judge`'s accuracies at levels the runs clear. Verify that a fresh `make eval-generation` passes the gate except for metrics reported as new
- [ ] 6.3 Accept the baselines explicitly with `make eval-accept SUITE=generation` and `make eval-accept SUITE=generation-judge`, and commit `evals/baseline.json`. Note in DECISIONS.md that this is a change of judge, not an improvement, and that earlier `generation` runs are not comparable

## 7. Jev review

- [ ] 7.1 Run the Jev review checklist (docs/rules/jev-usage.md §7) over the new request and record each answer in the DECISIONS.md section: closed and atomic Nouls; nothing code can compute; one request per state; minimal state with backticked paths; positive polarity with aligned criteria; the 0.5 cut and the 0.2–0.8 band; the no-key and failure fallback; the pinned model logged; the shared client with retries; tested on labelled English and Bulgarian inputs (5.2)
- [ ] 7.2 Confirm that the production answer check is untouched: its questions are byte-identical, `git diff` on `JevAnswerCheck.cs` is empty, and `make eval SUITE=answer-check` is unchanged within tolerance

## 8. Documentation

- [ ] 8.1 Update the README.md evals section: Jev grades `generation` and its metrics, the `generation-judge` suite, the HTML report, and the `generation` tolerance sentence with its re-measured values
- [ ] 8.2 Extend the Jev line in CLAUDE.md with "and grades answers in the eval". Finish the DECISIONS.md section, and verify that docs/*.md, openspec/project.md and .github/copilot-instructions.md still read true
- [ ] 8.3 Run `make docs` to regenerate the README commands table from the Makefile help text (never edit inside a `generated:` block by hand), then run `make docs-check` and verify it passes
