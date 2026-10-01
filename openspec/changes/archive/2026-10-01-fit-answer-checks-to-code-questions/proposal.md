# Proposal: fit the answer checks to code questions

## Why

The content guard and Jev's answer check were written and calibrated for the billing domain only, and on chat turns
about the repository's own code (`search_codebase`, mcp-code) they mostly raise false alarms. In the stored turns
analysed on 2026-10-01, `guardrail_withheld` fired on 10 of 45 codebase turns and on 0 of 175 documentation turns, and
`answer_not_grounded` on 21 of 45 against 7 of 175. As a result 27 of the 45 codebase turns reached the review queue.
Cyrillic codebase questions were flagged not grounded 19 times out of 32 (median grounded 0.41). Latin-script ones were
flagged once out of 12 (median 0.80). Nine turns were reviewed by hand:

- All five guard withholds were false positives. Only `guard_to_ai` fired (0.85–0.97), on prompt files the index
  holds; the other content questions stayed ≤ 0.25.
- Of the not-grounded flags, two were clear false positives and two were artefacts of the source cap. Four were true,
  and most of those were caused by retrieval gaps.

When most of the queue is noise, reviewers stop reading it. That also hides the true codebase findings.

The causes, confirmed in code:

1. **The guard withholds code that is about prompts.** The content context says `untrusted_text` was returned to "an
   AI billing assistant … a document excerpt, a billing record" (`JevGuardQuestions.ContentContext`). The code index
   holds `src/Maf.Lab.Api/Prompts/system.v*.md`, `SystemPrompt.cs`, `TestAgent/Instructions.cs`, `CLAUDE.md` and
   `docs/rules/jev-usage.md`, and all of them really are addressed to an AI, so `guard_to_ai` answers yes, as it
   should. `Guardrail.ScreenToolResultAsync` then removes the item entirely (`results.RemoveAt`), so neither the
   model nor the answer check sees even its path. The 0.85 threshold was measured only on billing texts: the guard
   suite screens every tool-side row as `eval_item` and has no codebase row.
2. **The source cap is first-come and never deduplicated.** `JevAnswerCheck.Cap` sends `state.Read` in order up to
   12,000 characters and cuts the item that crosses the limit. A turn that searches several times keeps its early,
   repeated results (the same chunk 3–7 times). It drops the later, targeted results the answer actually cites, and
   crowds out `previous_sources`.
3. **The answer check's context is billing-only.** It speaks of "fee billing and investment portfolios" and "document
   excerpts and records". It says nothing about paths, line ranges, symbols or quoted code, or about a Bulgarian answer
   drawn from English sources. `MinGrounded`/`MinRelevant` = 0.5 are provisional (DECISIONS §42, eight English
   billing questions). There is no review band, although jev-usage §4.5 asks one for a Noul. The model also writes
   U+2011 non-breaking hyphens inside paths and line ranges. That is a weak effect (r = −0.26), so here it is a
   hypothesis that code can remove at no cost.
4. **The codebase snippet is cut before the line that matters.** `CodeSearchService.Snippet` keeps the first 1,200
   characters of a chunk. It still reports the chunk's full `startLine`–`endLine`, so an answer can cite lines the
   model never saw, and the check cannot find them.

### Why Jev, and why not code or an LLM (jev-usage §2, §5)

The two judgements this change touches stay with Jev:

- **Screening a code snippet** asks "does this text address an AI, tell its reader to override rules, send data
  out…?". Each is a closed yes/no, needs language understanding, has to be answered for every snippet on the hot path,
  and code acts on the result by withholding or passing.
- **"Is every claim in the answer supported by these snippets?"** is a closed yes/no on state we pass. It is asked
  once per answered turn. An LLM judge would cost a second chat-model call per turn and return uncalibrated JSON. The
  rubric judge does exactly that, and only in the eval.

Everything code can compute exactly moves into code (§5):

- which domain a result came from;
- deduplication by `(path, start, end)` / `(docId, section)`;
- whether the answer cites a source, by a substring match on its path or file name;
- whether the sources fit under the cap;
- normalising U+2010/U+2011/NBSP;
- the review band;
- where a snippet's window falls.

Jev is still not a security boundary (§8). For a codebase item the guard stops withholding on `guard_to_ai` alone
(below). This is safe because the structural defences are unchanged: the envelope, no write tool in the codebase
domain, the tenant from the principal, and the user's approval of every write.

## What Changes

- **Guard, codebase-aware.**
  - A `search_codebase` item is screened with a codebase content context: prompt templates, agent instructions,
    string literals and docs in the repository are code being read, not instructions to this assistant. The question
    ids and the request shape stay the same.
  - For a codebase item, `guard_to_ai` is recorded but does not withhold on its own. The item is withheld only when
    `guard_override`, `guard_exfiltrate`, `guard_act` or `guard_cross_tenant` reaches the threshold (configuration:
    `Guard:CodebaseRecordOnly`).
  - Documentation, portfolio and every other tool keep today's context and threshold unchanged.
  - A withheld search item, in any domain, is replaced by a **stub** that keeps only identifiers that are not free
    text: for code `path`, `startLine`, `endLine`; for documentation `docId`; plus `withheld: true`. It is no longer
    removed. The stub is not a source. The answer check reads it as "withheld", so a claim about its content stays
    unsupported.
  - Calibration comes first. `evals/guardrail.jsonl` gains codebase rows: benign prompt files, agent instructions and
    string literals from the repository, and injections planted in repository-shaped files, one of them addressed only
    to the AI. The guardrail suite then screens each tool-side row as its own `tool`, so a codebase row gets the
    codebase context.
- **Answer-check sources.**
  - Sources are deduplicated: search items by `(path, start, end)` or `(docId, section)`, other results and previous
    envelopes by text.
  - They are ordered: the sources the answer cites first (for code, the answer contains the path or the file name),
    then the rest of this turn's, then `previous_sources`, cited ones first.
  - Items are sent whole, never cut.
  - If one of this turn's sources, or a cited previous source, does not fit under `MaxSourceChars`, the check sends no
    request and records `unchecked` with the reason `sources over cap`. Judging on a fraction produced false flags.
- **Answer-check judgement.**
  - The context is chosen by the turn's domain: a **codebase variant** whenever this turn's or the previous turn's
    sources came from `search_codebase`, and the current billing/portfolio context otherwise, so the billing calibration
    does not move.
  - In the codebase variant a source reads `path:start-end › symbol: code`. A path, line range, symbol, identifier or
    quoted code counts as supported when a snippet carries it. Meaning is judged across languages.
  - U+2010/U+2011/U+2012 are normalised to `-`, U+2013 to `-` between digits, and U+00A0/U+202F to a space. This
    applies to the answer copy in the Jev state and to the cited-source match, never to the stored answer.
  - **A review band.** Each Noul is `≤ NotGroundedAt` (or `NotRelevantAt`) → `not_grounded` / `not_relevant` plus the
    signal. `≥ PassAt` → `pass`. Anything between is a new verdict `uncertain`, recorded in the trace and the
    statistics with **no** review signal. The defaults are provisional (0.2 / 0.8, the jev-usage starting band) until
    the labelled set below sets them.
  - The existing `relevantFloor` / `groundedFloor` fields become the signal floors (`NotRelevantAt`, `NotGroundedAt`),
    so stored events and the statistics keep their meaning: below the floor, a signal.
- **Measured on labelled answers.**
  - A new `answer-check` eval suite replays fixed `{question, previous_question, answer, sources, previous_sources}`
    rows from `evals/answer-check.jsonl` through the production check. It makes no chat-model call and runs no tool.
    Each row declares an expected flag, a domain, a language and a split.
  - The set includes the reviewed codebase turns: the good answers are expected to pass and the wrong ones to be
    flagged. It covers English, Bulgarian and Latin-script Bulgarian, and keeps billing rows so the billing side cannot
    regress unseen.
  - `evals/generation.jsonl` gains codebase and Bulgarian questions for the live agreement metrics.
  - The chosen band and the guard's codebase numbers are recorded in a new DECISIONS.md section that supersedes the
    provisional floors of §42.
- **mcp-code snippet window.**
  - `search_codebase` returns the window of a chunk around the lines that match the query (the codebase BM25
    tokenizer's terms, densest run of hits), up to `SnippetMaxChars`.
  - `startLine`/`endLine` are the **window's** lines, so every cited line range is one the model saw.
  - A query with no lexical hit in the chunk (a Bulgarian phrase, for example) keeps the head of the chunk, as today.
- **Deferred**, each to its own change, because each alters the tool set, the system prompt or the queue's contract
  and needs its own evals:
  - a `read_code(path, startLine, endLine)` tool (a new tool means a selection-eval rerun, a system-prompt change and
    a tenant/path-safety review);
  - short-circuiting an identical repeated tool call (it changes the tool loop of every domain);
  - severity or priority in the review queue (`FeedbackEndpoints` lists every turn with a signal; `uncertain` raises
    none, so the queue shrinks without a contract change);
  - moving `MaxSourceChars` (Jev bills per input token, and a TypeSafe billing discrepancy is open; the rate of
    `sources over cap` is measured first).

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `injection-defense`:
  - codebase items are screened with a codebase context, and `guard_to_ai` on its own does not withhold them;
  - a withheld search item becomes a stub instead of being removed.
- `answer-check`:
  - sources are deduplicated, ordered cited-first, sent whole, and a turn over the cap is unchecked;
  - there is a codebase context variant;
  - hyphens and spaces are normalised;
  - a review band adds the verdict `uncertain` with no signal.
- `turn-tracing`: the `answer.check` event carries the band (the pass threshold) and the verdict `uncertain`, and the
  `guardrail` event records the record-only questions.
- `jev-statistics`: the answer-check section counts `uncertain` answers.
- `codebase-search`: a snippet is the window around the matching lines, and its line range is the window's.
- `eval-harness`:
  - the guardrail suite screens each row as its tool and covers codebase rows;
  - a new `answer-check` suite measures the check on labelled answers;
  - the generation dataset covers codebase and Bulgarian questions.

## Impact

- **Code:**
  - `src/Maf.Lab.Api/Agent/Jev/JevGuardQuestions.cs` (codebase content battery), `JevGuard.cs` (screen with a
    battery), `Guardrail.cs` (per-tool battery, record-only questions, stub instead of `RemoveAt`, options);
  - `src/Maf.Lab.Api/Agent/Jev/JevAnswerCheck.cs` (options, contexts, normalisation, source selection, verdicts,
    title, trace);
  - `src/Maf.Lab.Api/Agent/ChatTurnRunner.cs` (`Read` keeps a key per item, the answer-check call passes the domain);
  - `src/Maf.Lab.Api/Agent/JevStatistics.cs`, `src/Maf.Lab.Domain/Jev/JevStatsContracts.cs` (an optional `uncertain`
    count);
  - `src/Maf.Lab.CodeSearch/Tools/CodeSearchService.cs` (the window);
  - `src/Maf.Lab.Eval` (the `AnswerCheckSuite`, a dataset loader, the guardrail suite's tool, the `all` list);
  - `web/src/monitor/traceData.ts`, `web/src/jev/JevPage.tsx`, `web/src/api/types.ts` (the new verdict, optional);
  - the Makefile (`eval-answer-check`);
  - the CI Jev stub (unchanged answers, and it must accept the new battery).
- **Data:** `evals/guardrail.jsonl`, `evals/answer-check.jsonl` (new), `evals/generation.jsonl`,
  `evals/baseline.json` (only after runs are read).
- **APIs:**
  - Tool results: `search_codebase`'s line range now describes the returned text, and a withheld search item is a
    stub.
  - The stats contract gains an optional field. No route, project, model setting or load-balancer location changes.
    No package moves.
- **Progress:** the new `make eval-answer-check` / `make eval SUITE=answer-check` reports one `answer-check i/N` line
  per case through the harness's progress, determinate over the known number of rows, like every other suite. No UI
  action is added.
- **Rules kept:** the tenant still comes from the principal only; no new Qdrant query path; tool results stay DTOs;
  logs carry verdicts, scores and counts, never a question, answer, snippet or path list; `JEV_MAF_LAB` stays in the
  bearer header only; the Jev model stays pinned at `jev-1.13.0`.

## Documentation impact

- `docs/trace-events.md`:
  - the `guardrail` row: codebase items are screened with the codebase context, record-only questions, a withheld
    search item is a stub;
  - the `answer.check` row: deduplicated and cited-first sources, `sources over cap`, the codebase context, the band
    fields and the `uncertain` verdict.
- `docs/http-api.md`: `JevStatsReport.answerCheck` gains the optional `uncertain` count.
- `README.md`: the eval suite list and the `make eval` / `eval-answer-check` rows. The Makefile help table is a
  `generated:` block, rewritten by `make docs`.
- `DECISIONS.md`: a new section with the guard's codebase measurement and the answer check's calibrated band,
  superseding the provisional floors of §42.
- Not affected: `CLAUDE.md`, `openspec/project.md` and `.github/copilot-instructions.md`. None of them describes the
  guard's contexts, the answer check's floors or the eval suite list beyond `make eval SUITE=selection`, which stays
  true.
