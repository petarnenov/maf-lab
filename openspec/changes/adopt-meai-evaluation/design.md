# Design

## Context

- `GenerationSuite` (src/Maf.Lab.Eval/Suites/GenerationSuite.cs) runs each of the 12 cases in `evals/generation.jsonl`
  through the live agent, builds a context string from `turn.Sources`, and calls `RubricJudge.ScoreAsync`: one
  `gpt-oss:120b` request returning `{faithfulness, relevance, reason}` on a 1–5 scale, normalised to 0–1. A case passes
  at 0.75 (`RubricPass`), and `JevMetrics` compares the turn's `AnswerCheck` with that mark.
- Every answered turn already carries Jev's production answer check (`TurnResult.AnswerCheck`, DECISIONS §42). It is
  one request with two Nouls over the whole answer (`answer_relevant`, `answer_grounded`). The state is
  `{ user_question, previous_question, answer, sources, previous_sources }`. Sources are chosen by
  `AnswerSources.Select` under `Jev:AnswerCheck:MaxSourceChars` (12000), and the billing and codebase contexts are
  separate (`Questions` / `CodeQuestions`).
- §42 recorded what that check gets right and wrong:
  - three runs with identical verdicts (probabilities move by a few hundredths, measured in this change);
  - g-04 was caught where the rubric missed it;
  - g-01 was flagged while correct;
  - no single floor separated the two, because a whole-answer Noul cannot say *which* claim is unsupported.
- `answer-check.jsonl`: 39 labelled answers (`unsupported`, `offTopic`, domain, language en/bg, design/holdout
  split). The `answer-check` suite measures the production check on them.
- `JevClient` (src/Maf.Lab.Retrieval/Jev/JevClient.cs) is the singleton. It has the pinned model, the circuit breaker
  and retries, and `AskAsync(state, questions, timeoutSeconds, ct)` returns a `JevOutcome` with a failure reason
  instead of throwing. In tests, `FakeJev` stands in for it.
- The accepted `generation` baseline (2026-10-03) has faithfulness 1, relevance 1, sourceRecall 0.9167 and Jev
  agreements of 1. `relevance` has a 0.035 tolerance, measured as judge noise.

## Goals / Non-Goals

**Goals:**
- Grade answers with atomic Jev judgments whose verdicts repeat on the same input, one request per case, with all arithmetic in code.
- Tell *which* sentence or reference point failed, not only that the answer did.
- Measure the grade itself on labelled answers before trusting it, English and Bulgarian.
- Use `Microsoft.Extensions.AI.Evaluation` for what it is good at here: the evaluator contract, stored results and the
  HTML report.

**Non-Goals:**
- Changing the production answer check, its questions (byte-identical by §42's calibration), floors or timeout.
- LLM-prompt evaluators (`.Quality`), an LLM claim extractor, or a second judge model.
- Replacing the deterministic suites or `PresentationSuite`'s verdict judge.
- Showing the HTML report in `/evals`.

## Decisions

### D1. Packages: `Microsoft.Extensions.AI.Evaluation` and `.Reporting` 10.10.0
These are on the same release train as the pinned `Microsoft.Extensions.AI` 10.10.0. The grade is an `IEvaluator`
that returns `NumericMetric`s with diagnostics. It needs no `ChatConfiguration`, because Jev is not an `IChatClient`.
`.Reporting`'s `DiskBasedReportingConfiguration` stores each run's `ScenarioRunResult`s under `evals/reports/meai/`.
Response caching is off: there are no chat calls to cache. Measured: Jev gives the same verdicts on identical input, with probabilities moving 0.01–0.05 between runs (DECISIONS §78).
*Rejected:*
- `.Quality` (Groundedness, Relevance, Completeness, Retrieval): LLM prompts with 1–5 grades bring back the noise,
  the latency and the broad judgments this change removes.
- Ragas and DeepEval: a Python runtime for the same LLM-judge approach.

### D2. Sentences are cut by code
`AnswerSentences.Split(answer)` is a pure function. It splits on:
- line breaks;
- list markers (`-`, `*`, `1.`);
- table rows (a row is one sentence);
- sentence ends: `.`, `!`, `?` or `…` followed by whitespace and an upper-case Latin or Cyrillic letter or a digit.

Text inside back-ticks and fenced code is never split, so paths such as `src/X.cs:12-40` and decimals stay whole.
Headings and empty pieces are dropped. The splitter is unit-tested on English, Bulgarian, Latin-script Bulgarian,
markdown lists, tables and code answers taken from `answer-check.jsonl`.
*Rejected:* claim extraction by an LLM, which is generation (System Two), noisy, and one more request.

### D3. The Jev request (one per case)
Sent through `JevClient.AskAsync` with the shared pinned model (`jev-1.13.0`) and timeout
`Evals:Judge:TimeoutSeconds` (default 10: an eval, not a turn).

**State.** The fields are only what the questions need:

```json
{
  "user_question": "…",
  "previous_question": "",
  "answer_sentences": ["…", "…"],
  "sources": ["docId › section: text", "path:12-40 › Symbol: code"],
  "reference_points": ["…", "…"]
}
```

- `sources` are chosen and formatted by `AnswerSources.Select` with the production cap, so the grade sees what the
  check sees.
- The billing or codebase context text is chosen as in production.
- `previous_question` is empty for generation cases, which are first turns. A labelled follow-up in
  `answer-check.jsonl` carries it, and its previous sources follow this turn's in `sources`, as the check reads them.

**Questions.** Generated in code: one set per sentence `i`, per reference point `j` and per source `k`, plus one for
the whole answer. All are Nouls with a `{ context, question }` instructions object (the guard's style, §35) and
true/false criteria.

| Id | Instructions | Criteria (true / false) |
|---|---|---|
| `claim_i` | Does `answer_sentences[i]` state a fact, figure, name, code, date, path or step? | states one / greeting, offer of help, what the assistant can do, "I don't know", a question back |
| `supported_i` | Is what `answer_sentences[i]` states supported by `sources`? (codebase: the code-context wording from `CodeQuestions`, across languages) | every fact in it appears in or follows from a source / it states something no source holds or a source contradicts, including when `sources` is empty |
| `stated_j` | Does any of `answer_sentences` state what `reference_points[j]` says? | states it, in any wording or language / leaves it out or states only part of it |
| `contradicts_j` | Does any of `answer_sentences` conflict with `reference_points[j]`? | states something incompatible with it / agrees or does not mention it |
| `on_subject_k` | Does `sources[k]` address the subject of `user_question`? | about the same subject / about something else |
| `answer_relevant` | Does `answer_sentences` together address what `user_question` asks? (the production relevance wording) | the production `RelevantYes` / `RelevantNo` |

`supported_i` and `claim_i` are separate Nouls, so each asks one condition and has positive polarity (§4.2). Code
combines them. All the questions share the state, so they go in one request (§4.4). The volume is one round trip,
whatever the count.

**Size.** The questions are bounded by `Evals:Judge:MaxSentences` (60) and the production source cap. Over the cap,
the first 60 sentences are graded, and the case carries `truncated` in its reason. The estimate is about 6k input
tokens at the cap, well under jev-1.13's 64k and 32k limits.

**Thresholds** (read-only eval, so the lowest risk class):
- A Noul is yes at ≥ 0.5, the point where Jev finds yes likelier.
- The 0.2–0.8 band (§4.5) is not a decision. It is counted into `judgeUncertain`, so a drift toward the middle shows.
- The pass mark for `faithfulness` stays 0.75. `relevance` must be 1.
- The thresholds are revisited only from `generation-judge` runs, never to make a `generation` run pass.

**Fallback.**
- No key: the suite refuses to start, as `answer-check` does.
- A failed request (timeout, error status, circuit open, missing answer): the case scores 0 on every judged metric
  with `judge failed: <reason>`, and the run continues.
- There is no LLM fallback. A silent switch of judge would mix two scales in one run.

**Logs.** Each request logs only numbers and ids: model, `usage`, latency, question count and case id. Never text.

### D4. Metrics from the answers (code)
- `claims` = {i : claim_i ≥ 0.5}.
- `faithfulness` = |{i ∈ claims : supported_i ≥ 0.5}| / |claims|, or 1 when `claims` is empty.
- `relevance` = answer_relevant ≥ 0.5 ? 1 : 0.
- `completeness` = |{j : stated_j ≥ 0.5}| / |points|.
- `referenceAgreement` = 1 − |{j : contradicts_j ≥ 0.5}| / |points|. It is named so that higher is better, which the
  regression gate assumes.
- `retrievalJudged` = |{k : on_subject_k ≥ 0.5}| / |sources|, or 1 when nothing was read.
- `judgeUncertain` = the share of all the case's Nouls in [0.2, 0.8].

`JudgeScore` becomes a record of these numbers plus the lists behind them: unsupported sentences, missed points,
contradicted points and off-subject sources. `JevMetrics` reads `Faithfulness` and `Relevance` as before.

### D5. The evaluator and the report
`JevGenerationEvaluator : IEvaluator` wraps D3 and D4. It returns one `NumericMetric` per metric, with the reason
and the lists as `EvaluationDiagnostic`s, and the raw Noul probabilities in the metric's metadata. The suite runs it
through a `ScenarioRun` per case, with the scenario named after the case id and the execution named after the run id.
At the end, the library's HTML writer renders `evals/reports/<runId>.html` from the stored results, which gives trends
across executions. If the writer is not public API at 10.10.0, task 1.2 switches to the `.Console` tool as a local
tool manifest and records why.

### D6. `generation-judge` suite
`GenerationJudgeSuite` runs without the agent. It uses `answer-check.jsonl` rows for faithfulness and relevance
against `unsupported` / `offTopic`, and `generation-judge.jsonl` rows for stated and contradicted per point.
- It calls the same `JevGenerationEvaluator`, with reference points empty for answer-check rows. Code drops the
  completeness questions when there are no points.
- It reports accuracy overall and per domain, language and split. It also reports the production check's accuracy on
  the same rows, computed by the existing `answer-check` metrics code, so the two read side by side.
- `generation-judge.jsonl` holds 16 hand-labelled rows: 8 billing and 8 codebase, half Bulgarian. They are built from
  recorded `generation` answers with a reference point removed, paraphrased or contradicted.

### D7. Agreement metrics
`jevGroundedAgreement` / `jevRelevantAgreement` keep their definition: the production check against the grade's
pass. They now compare a whole-answer Noul with a sentence-level grade from the same model family. The
relevance agreement will sit near 1, since it is the same wording on almost the same state. That is expected and
recorded; a drop then points at the production side (the cap, the timeout, the previous-question context). The
grounding agreement is the useful one for tuning `MinGrounded`.

## Migration Plan

1. **Side by side.**
   - `generation` runs both judges over the same turn and reports:
     - the Jev grade under the metric names above;
     - the rubric as `rubric:faithfulness` / `rubric:relevance`;
     - `judgeAgreement:faithfulness|relevance`: the share of cases where both land on the same side of the pass mark.
   - The `rubric:*` and `judgeAgreement:*` metrics have no thresholds and are never accepted into the baseline.
   - `generation-judge` runs as a suite of its own.
2. **Measure.**
   - Five `generation` runs.
   - Two `generation-judge` runs, whose verdicts must agree (D1 determinism).
3. **Gate** (all must hold):
   - `generation-judge`: grade accuracy on `answer-check.jsonl` ≥ the production check's accuracy on the same rows,
     overall and for Bulgarian. Point accuracy on `generation-judge.jsonl` ≥ 0.9 overall and ≥ 0.85 for Bulgarian.
   - `generation`: no `judge failed` case. Every case where the two judges disagree is read and recorded in DECISIONS.md
     with which judge was right. g-01 and g-04 are named explicitly.
   - `generation`: the run-to-run range of `faithfulness` and `relevance` is no larger than the rubric's over the same
     five runs.
4. **If the gate holds:**
   - Delete `RubricJudge`, the `rubric:*` and `judgeAgreement:*` metrics.
   - Set tolerances from the five runs' ranges, now the agent's variation only, with each override recording what it
     was measured from.
   - Add thresholds for `completeness`, `referenceAgreement`, `retrievalJudged` and for `generation-judge` at levels the
     runs clear.
   - Accept both baselines explicitly (`make eval-accept SUITE=generation`, `…SUITE=generation-judge`) and record the
     acceptance as a change of judge, not an improvement.
5. **If the gate fails:** stop. Keep `RubricJudge`, record the numbers in DECISIONS.md, and revise this change with the
   user. Likely revisions: the splitter, a criterion's wording, or the 0.5 cut.

Rollback after the switch is a revert of the switch commit plus the earlier baseline from git history.

## Risks / Trade-offs

- [Jev is English-first. Bulgarian answers may be graded worse] → `generation-judge` reports Bulgarian separately, and
  the gate requires it. A known-bad split is fixed in the splitter, not by lowering the bar.
- [The eval grade and the production check share a model, so their errors may correlate] → The grade is checked
  against human labels (`generation-judge`), not only against the check. The rubric's disagreements are read case by
  case in the comparison.
- [Sentence splitting misreads tables, code or abbreviations] → The splitter is a tested pure function over real
  answers. One sentence too many or too few changes one share by a little and never crashes the request.
- [A long answer with many sources inflates the question count] → `MaxSentences` and the production source cap
  bound it. The case says `truncated`.
- [Token cost: Jev charges per input token] → `usage` is logged per request and summed in the run's settings (`judgeInputTokens`), so a run's
  cost can be read and compared.
- [`relevance` becomes binary, so a partial answer is no longer "3/5"] → Partial answers show in `completeness`, which
  is the more precise signal.

## Open Questions

- Whether the HTML writer is callable in-process at 10.10.0 (D5). Either way the spec and tasks stay the same.
