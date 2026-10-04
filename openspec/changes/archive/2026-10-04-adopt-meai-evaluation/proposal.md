# Proposal

## Why

The `generation` suite grades answers with `RubricJudge`, a 46-line prompt written on day 3 to get a first baseline. It
asks `gpt-oss:120b` for two 1–5 grades in "JSON mode". Several problems follow from that:
- The grade is noisy: `relevance` carries its own 0.035 tolerance only because the judge moves between identical runs.
- It is slow: one chat-model request per case.
- It is broad: one number per answer hides which sentence is unsupported.
- It disagreed with Jev on g-04, and Jev was right (DECISIONS §42).

Grading an answer is a set of closed, atomic judgments: is this sentence a claim, is it supported, does the answer
state this reference point, is this source on the question. That is exactly what Jev is for (docs/rules/jev-usage.md
§2 C/F/G). Jev's answers are calibrated, give the same verdicts on the same input (§42: "the runs were identical"), and come
in one request per case whatever the number of questions. Code does the counting.

`Microsoft.Extensions.AI.Evaluation`, the evaluation library of the `Microsoft.Extensions.AI` 10.10.0 already in use,
provides the harness around a judge: the evaluator contract, stored results and an HTML report with trends. Its
LLM-prompt evaluators (`.Quality`) would bring back the noise and cost this change removes, so they are not used.

## What Changes

- Add `Microsoft.Extensions.AI.Evaluation` and `.Reporting` 10.10.0 to `Maf.Lab.Eval`, recorded in DECISIONS.md in the
  same commit. `.Quality`, `.NLP` and `.Safety` are not added.
- **Jev grades `generation`.** There is one Jev request per case, through the shared `JevClient` with the pinned model
  (`jev-1.13.0`). The state is the question, the answer split into sentences by code, the sources the turn read
  (selected and formatted as the production answer check does), and the case's reference points. Code computes:
  - `faithfulness` = supported claim sentences / claim sentences (1 when the answer makes no claim);
  - `relevance` = whether the answer addresses the question;
  - new `completeness` = reference points the answer states / reference points;
  - new `referenceAgreement` = 1 − the share of reference points the answer contradicts;
  - new `retrievalJudged` = sources on the question's subject / sources read;
  - new `judgeUncertain` = the share of the case's Nouls in the 0.2–0.8 review band (diagnostic, no threshold).
  Each case's failure names the unsupported sentences and the missed or contradicted points.
- The judge is wrapped as an evaluator of the library. Each run's results are stored under `evals/reports/`
  (git-ignored) and written as an HTML report beside the JSON and Markdown ones.
- `generation.jsonl` rows gain `referencePoints`: the reference answer split by hand into atomic statements, English
  and Bulgarian.
- **New `generation-judge` suite** measures the judge itself without the agent, as `answer-check` does for the
  production check. It runs over `answer-check.jsonl`'s labelled answers (unsupported / off-topic, English and
  Bulgarian, design and held-out splits) and a new `generation-judge.jsonl` of answers labelled per reference point
  (covered / contradicted). It reports accuracy overall and per language, beside the production check's accuracy on the
  same rows.
- **Side by side first.** Until the gate in design.md passes, `generation` also runs `RubricJudge` over the same turn
  and reports `rubric:*` and `judgeAgreement:*`. If the gate passes: `RubricJudge` goes, the tolerances are re-measured
  and the baseline is re-accepted explicitly. **BREAKING** for the `generation` baseline: `faithfulness` and
  `relevance` change meaning (sentence share and a yes/no instead of 1–5 prose grades), so earlier runs are not
  comparable.
- `jevGroundedAgreement` / `jevRelevantAgreement` now compare the production check (one Noul over the whole answer)
  with the eval's sentence-level grading. They remain the evidence for tuning the production floors.

**Why Jev, not code or an LLM (jev-usage §2, §5).**
- Every question is closed and atomic: a Noul per sentence, per reference point and per source.
- It needs language understanding that code lacks.
- It is answerable from the state passed.
- Code acts on each answer by thresholding and counting.
- No question asks Jev to count, do arithmetic or compare dates.
- An LLM is kept only where text must be written: the agent's answer.
- Splitting the answer into sentences is code, not Jev and not an LLM. Claim extraction by an LLM would reintroduce the
  noise.

**Progress.** No make target is added. `make eval-generation` keeps its per-case progress line with the new scores.
`make eval SUITE=generation-judge` shows one line per case over the known number of cases, as `answer-check` does.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `eval-harness`:
  - **JSONL datasets:** `generation.jsonl` gains reference points, and `generation-judge.jsonl` is added.
  - **Metrics:** generation is graded by atomic typed judgments with the metrics above, instead of "an LLM judge with a
    fixed rubric".
  - **Generation suite and Jev's answer check:** the agreement metrics compare against the sentence-level grade.
  - **New:** the judge is measured on labelled answers.
  - **New:** a browsable per-run report.

## Impact

- Code in `src/Maf.Lab.Eval`:
  - `Suites/GenerationSuite.cs`;
  - a new Jev grader and evaluator wrapper, the sentence splitter and the `generation-judge` suite;
  - `Datasets/`, `Program.cs` and `Reports/`;
  - `Suites/RubricJudge.cs`, removed at the switch.
- `JevAnswerCheck`'s source selection (`AnswerSources.Select`) and its context texts are reused, not copied.
- Tests in `tests/Maf.Lab.Tests`: `EvalHarnessTests`, `AnswerCheckEvalTests` (they use `JudgeScore`), and new grader,
  splitter and suite tests with `FakeJev`.
- Dependencies: two new `PackageVersion` entries.
- Data:
  - `evals/generation.jsonl` gains `referencePoints`;
  - new `evals/generation-judge.jsonl`;
  - `evals/baseline.json`: `generation` re-accepted, `generation-judge` accepted;
  - `src/Maf.Lab.Eval/eval.json`: thresholds and tolerances.
- Cost and latency:
  - One Jev request per case, about 3–6k input tokens. The `usage` and `model` of each request are logged as numbers.
  - Each case replaces a `gpt-oss:120b` request.
  - During the comparison both judges run.
- Needs `JEV_MAF_LAB`: `generation` and `generation-judge` refuse to run without it, rather than report a judge that
  graded nothing. Evals are on demand, so CI is unaffected.

## Documentation impact

- `DECISIONS.md`: a new section covering why Jev grades and the library only reports, the package versions, the
  request (state, questions, thresholds, fallback, model), the comparison numbers, the gate's outcome, the re-measured
  tolerances and the baseline re-acceptance.
- `README.md`, evals section:
  - who grades `generation` and its metrics;
  - the `generation-judge` suite;
  - the HTML report;
  - the `generation` tolerance sentence with the re-measured values.
- `Makefile`: the `eval` target's help text lists `generation-judge` among the suites. The help text feeds the README's
  generated commands table, so `make docs` regenerates it.
- `CLAUDE.md`: the Jev line ("routes data turns (api) and judges search relevance (mcp-retrieval)") gains "and grades
  answers in the eval".
- `docs/*.md`, `openspec/project.md`, `.github/copilot-instructions.md`: not affected. No route, project, model or
  load-balancer location changes, and the Jev section of project.md stays true.
