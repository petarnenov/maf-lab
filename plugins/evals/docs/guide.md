## Evals — when you must run them

```bash
make eval                     # all gated suites against the running stack's MCP (graph-depth runs only when named)
make eval-selection           # or eval-retrieval / -generation / -injection / -confirmation / -intent / -guardrail / -answer-check / -code-route / -presentation / -graph-depth / -retrieval-backends / -a2a
dotnet run --project service/runner -- --suite retrieval --rerank   # extra flags: use the CLI directly
dotnet run --project service/runner -- --import-feedback --suite retrieval
```

Evals run **on demand**, not on every commit. They are **required** before merging any change to:

- the **system prompt** (`src/Maf.Lab.Api/Prompts/*`) → `selection`, `generation`, `injection`
- a **tool description or schema** (`src/Maf.Lab.{Retrieval,Portfolio,CodeSearch}/Tools/*`) → `selection`
- the **model** (chat or embedding, `Models:*`) → `all`
- the **tool set** (adding/removing a tool) → `selection`, `injection`
- the **chunking or retrieval configuration** (chunkers, `Indexing:*`, `Retrieval:*`, BM25) → `retrieval`, `generation`
- **query normalisation** (`Retrieval:NormalizeQueryLanguage`, `Retrieval:CorpusLanguage`, the translation model) → `retrieval`
- a **Jev screening or check** (the guard's batteries or `Guard:*`, the answer check's questions or `Jev:AnswerCheck:*`) →
  `guardrail`, `answer-check`; both call Jev alone, with no chat model and no tool
- the **generation grade** (`service/runner/Judging/*`, `Evals:Judge:*`) → `generation-judge` first (labelled answers,
  no agent), then `generation`
- the **code graph** (the builder in `src/Maf.Lab.Indexing/Graph`, or a trace or impact depth in `CodeGraphTools`) →
  `graph-depth`
- a question about the **vector store itself** (Qdrant versus Neo4j) → the billing plugin's `neo4j-chunks` target, then
  `retrieval-backends`, a comparison like `graph-depth`: the retrieval cases on Qdrant and on an eval-only Neo4j search
  over the same copied chunks, side by side, never gated (neo4j-retrieval-spike)
- the **code-route question** (`CodeToolRouter`, `Jev:RouteCodeTools`, `Jev:MinCodeRouteConfidence`) → `code-route`, which
  asks Jev alone whether each code question would start with the right graph call or the search, and `intent`

`graph-depth` is a **comparison**, not a gate. It runs the same labelled code-graph cases with the graph tools pinned to
2, 3 and 4 calls, on a codebase server it starts itself (`CodeSearch:GraphDepthPin`, never set in a deployment). It
reports two layers side by side:
- **structural**, with no model: recall, also split by the depth a case needs, nodes, tokens, truncation and latency;
- **end-to-end**: the Jev grade `generation` uses (faithfulness against what the turn read, relevance), and whether
  the answer names what it needed.

Beside the all-turn end-to-end scores it reports each variant's scores over its own turns that called a graph tool
(`:graph`, with `graphTurns`), and over the cases that called the graph in every variant (`:common`, with
`commonCases`). Read a depth's effect from the `:common` scores, where the three depths answered the same questions
with the graph. It has no thresholds, is never compared with or accepted into the baseline, and `all` does not run it.
`make eval-graph-depth STRUCTURAL=1` runs the structural layer alone, with no chat model or Jev key. End-to-end numbers
vary between runs, so compare two runs before reading a difference as a result.

### How `generation` is graded

Jev grades every answer, in **one request per case**. Code cuts the answer into sentences, and every question is a
yes/no about one item:
- per sentence: does it state a fact, and is that fact supported by what the turn read. A place the sentence cites
  (`path:start-end`, "Section 3 → Step 2") is looked up in code, not asked of Jev: a place no source holds makes the
  sentence unsupported, and a found one is masked before Jev reads the rest;
- per reference point (`referencePoints` in `generation.jsonl`): does the answer state it, and does it contradict it;
- per source: is it on the question's subject;
- once: does the answer address the question.

Code counts the yeses (a Noul is yes at 0.5) into these metrics:
- `faithfulness`: supported claim sentences over claim sentences;
- `relevance`;
- `completeness`: points stated;
- `referenceAgreement`: 1 − points contradicted;
- `retrievalJudged`: sources on the subject;
- `judgeUncertain`: answers in the 0.2–0.8 band, a diagnostic.

A failed case names the unsupported sentences, the cited places no source holds, and the missed or contradicted
points. Without `JEV_MAF_LAB` the suite refuses to run rather than report a grade of nothing.

The agent answers differently on every run, so `make eval-generation` runs the 36 cases **three times** and gates
the mean (`Evals:Repeat`, or `REPEAT=N`). Every run keeps its own report. `make eval-accept SUITE=generation REPEAT=10`
accepts the mean of ten runs as the baseline.

The grade is an evaluator of `Microsoft.Extensions.AI.Evaluation`. Each case is kept under `evals/reports/meai/`, and
every run writes `evals/reports/<runId>.html` beside its JSON and Markdown: every case with its scores, the sentences
behind them, and how they moved over the last ten runs.

`make eval SUITE=generation-judge` measures the grade itself, with no agent, and reports four variants:
- `grade`: the grade on `answer-check.jsonl`'s labelled answers;
- `check`: the production answer check on the same rows;
- `points`: the grade on `generation-judge.jsonl`'s reference points labelled stated or contradicted;
- `sentences`: the grade per sentence on `generation-sentences.jsonl` — claims, support, and citations correct and
  invented, apart.

Each variant reports accuracy overall and per domain, language and split. See DECISIONS.md §79.

### Not getting worse

The thresholds answer "is this usable at all"; the **baseline** answers "is this worse than it was".
`evals/baseline.json` is committed and records the metrics this repository has accepted, per suite and variant.
Every run compares against it and fails when a metric drops by more than the tolerance, naming what moved:

```
✗ REGRESSION retrieval/hybrid mrr: 0.9 → 0.647 (-0.253)
↑ improved retrieval/hybrid recall@5: 0.5 → 0.687 (+0.187)
· within tolerance retrieval/hybrid recall@5:bg: 0.681 → 0.66 (-0.021)
make eval-accept        # run the suites and accept their metrics as the new baseline (then commit it)
```

A run never moves the baseline by itself, and a run below its thresholds is refused rather than blessed. A metric
the baseline does not mention is reported as *new* and one it mentions but the run did not produce as *missing* —
both are how a rename silently switches the gate off. The tolerance is `Evals:RegressionTolerance` (0.02), overridden
per metric in `Evals:RegressionTolerances`, each override carrying what it was measured from: in `retrieval`,
`recall@5:bg` and `recall@20` use 0.025, because a non-English query is translated by a live model and moved about
0.021 over five runs while `recall@5:en` did not move at all; in `generation`, each
tolerance is the range of every 3-run mean over ten runs (faithfulness 0.035), and a `generation-judge` tolerance is
never below one labelled item. `/evals` plots any metric across past runs with the baseline marked.

Datasets are JSONL under `evals/`; reports land in `evals/reports/` (JSON for the `/evals` page, Markdown for humans).
Three of them are not run by the harness: `a2a-conformance.jsonl` is run by the protocol plugin’s probe, and
`injection-a2a.jsonl` and `ui-events.jsonl` are fixture sets driven by tests — the first through the verdict check
and the write flow, the second replayed through the browser's reducer. `ui-events.jsonl` holds runs captured from
a running stack by `scripts/capture_ui_events.sh`; re-capture it when what the server emits changes. The current
recording predates the generic interrupt shape (DECISIONS §85): re-record it on a stack with billing and monitor installed.
Thresholds are configuration (`service/runner/eval.json` → `Evals:Thresholds`); the command exits non-zero when a
suite falls below them. Labeled production feedback (UI → `/admin/feedback`) is appended to the datasets, so the next
run includes it. The contextual-retrieval variant needs a second index:

```bash
Qdrant__Collection=maf_chunks_ctx Qdrant__MetaCollection=maf_chunks_ctx_meta \
  dotnet run --project src/Maf.Lab.Indexing -- index --contextual on
dotnet run --project service/runner -- --suite retrieval --contextual
```
