# Tasks

## 1. Calibration data first (no code change reads it yet)

- [x] 1.1 Add `search_codebase` rows to `evals/guardrail.jsonl` as D9 lists. Benign rows: excerpts of the prompt
      templates, agent instructions, string literals, a test that asserts on an injection string, and guardrail
      dataset lines. Malicious rows: injections planted in a C# comment, Markdown and a string literal (override +
      exfiltrate, act now, other firms, and one addressed to the AI only), in en/bg/bg-latn, with the split fixed before
      any run. Verify with the dataset loader test (`make test`) and a count per side, tool, language and split.
- [ ] 1.2 Create `evals/answer-check.jsonl` as D9 describes:
      - the nine reviewed turns;
      - codebase rows up to at least 4 per language × {supported, unsupported};
      - at least 8 billing rows, g-01 and g-04 among them;
      - the split fixed before any run, and no client data.
      Verify with a new loader test that rejects a row missing a field and accepts every committed row.
      **Open:** the codebase rows are hand-written reconstructions of the reviewed kinds; the nine reviewed turns themselves live in the stack's api database and were not available to the implementing agent. Export them (question, answer, the turn's deduplicated sources) into `evals/answer-check.jsonl`, keep their split fixed, and re-run `make eval-answer-check`. Everything else in 1.2 is done and its loader test passes.
- [ ] 1.3 Add codebase questions in English and Bulgarian, with repository paths as `expectedDocIds`, to
      `evals/generation.jsonl`. Verify that the loader test passes and that `make eval SUITE=generation LIMIT=…` lists
      them in its progress.
      **Open:** rows added and the loader test passes; the progress listing needs the paid generation run of 7.3 (`make eval SUITE=generation`, the codebase rows are the last four of twelve).

## 2. Guard: codebase battery, record-only questions, stubs

- [x] 2.1 Add `JevGuardQuestions.CodeContent`: the same ids and question sentences, with the codebase context and
      "does not count" halves from design R1. Let `JevGuard.ScreenContentAsync` take the battery. Verify with a unit
      test that snapshots both batteries' JSON, shows that no question carries screened text, and confirms the billing
      battery is byte-identical to before.
- [x] 2.2 In `Guardrail`, choose the battery by tool and apply `GuardOptions.CodebaseRecordOnly` (default
      `["guard_to_ai"]`) to codebase items only. Add `context` and `recordOnly` to the `guardrail` trace event. Verify
      with `GuardrailTests` using FakeJev:
      - `guard_to_ai` 0.97 on a `search_codebase` item → pass, no `guardrail_withheld`, 0.97 in the trace;
      - `guard_to_ai` 0.97 on a `search_documents` item → withheld;
      - `guard_override` 0.9 on a codebase item → withheld;
      - 0.6 → pass;
      - a timeout → unscreened, fail open.
- [x] 2.3 Replace `results.RemoveAt` with the stub from D3 (`path`, `startLine`, `endLine` or `docId`, and
      `withheld: true`, never `symbol`, `sectionPath` or `snippet`). Skip stubs in `Summarise` and in the sources, and
      write the withheld line in `Read`. Verify with tests:
      - the stub's JSON holds no text of the item;
      - the stub is not among the turn's sources;
      - the `tool.result` and envelope events carry the stub;
      - the answer check's sources name the place as withheld.
- [x] 2.4 Add `Guardrail.ScreenItemAsync(tool, text)` and make `GuardrailSuite` screen each tool row as its `tool` (no
      tool means today's path). Add per-tool breakdowns to `Metrics.Guardrail`. Verify with a suite unit test on
      FakeJev that the codebase context is sent for a `search_codebase` row and the billing context for the others.
- [x] 2.5 Make the CI Jev stub accept the codebase battery (same ids) and still answer `guard_*` from its fixed
      phrases. Verify with `make ci-e2e`, or the stub's own test.

## 3. Answer check: sources

- [x] 3.1 Change `state.Read` to `ReadItem(Key, Text, CitationNames, Domain)`, built in `ChatTurnRunner.Read` and the
      fixed-text paths. Parse the previous envelopes (`tool="…"`, paths) into `ReadItem`s in `PreviousReadAsync`.
      Verify with a unit test over a stored trace holding a `search_codebase` envelope.
- [x] 3.2 Add `AnswerText.Normalise` (D6). Verify with unit tests: U+2011 in `Guardrail.cs:153‑195` gives `-`; U+2013
      between digits gives `-`, while a dash between words is kept; NBSP and U+202F give spaces; the stored answer is
      unchanged.
- [x] 3.3 Add `AnswerSources.Select` (D4): dedupe, cited-first ordering, whole items, and `OverCap`. Verify with unit
      tests:
      - the same `(path, start, end)` three times is sent once, with duplicates = 2;
      - a late cited snippet comes first;
      - an over-cap current item gives `unchecked` / `sources over cap` with no request sent (FakeJev request count 0);
      - an uncited previous envelope is left out while a cited one is kept;
      - identical previous envelopes are sent once.

## 4. Answer check: context and band

- [x] 4.1 Add `JevAnswerCheck.CodeQuestions` (design R2) and choose it when any selected source's domain is codebase.
      Verify with a snapshot test of the codebase request, which carries no question, answer or source text in its
      instructions or criteria, and a test that a billing-only turn sends the unchanged `Questions`.
- [x] 4.2 Replace the single floors with the band (D7): `NotGroundedAt`/`NotRelevantAt` 0.2 and
      `GroundedPassAt`/`RelevantPassAt` 0.8, with `MinGrounded`/`MinRelevant` bound as aliases, and add the verdict
      `uncertain`. Update the `Signals`, the title (`in 0.20–0.80 — uncertain`) and the trace fields (`relevantPassAt`,
      `groundedPassAt`, `context`, `duplicates`). Verify with `AnswerCheckTests`:
      - grounded 0.35 → `uncertain`, no signal;
      - 0.15 → `not_grounded` + `answer_not_grounded`;
      - 0.85/0.9 → `pass`;
      - both 0.1 → both signals;
      - the alias override still binds.
- [x] 4.3 Count `uncertain` in `JevStatistics` and the optional `JevStatsContracts` field, and show it in
      `web/src/jev/JevPage.tsx`. Treat it as neutral in `web/src/monitor/traceData.ts` (no chip) and in `types.ts`.
      Verify with `JevStatsTests` (one pass, one uncertain → checked 2, uncertain 1; over-cap → no request) and the
      Vitest cases in `JevPage.test.tsx` and `MonitorPanel.test.tsx`.
- [x] 4.4 Let the generation suite report the uncertain share and count agreement as "no signal = pass". Verify with a
      unit test over a fake `TurnResult` set.

## 5. Codebase snippet window

- [x] 5.1 Implement the window in `CodeSearchService.Snippet`/`ToSnippet` (D8), with `StartLine`/`EndLine` shifted to
      the window. Verify with `CodebaseSearchTests`:
      - a 3,000-character chunk whose matching identifier line (`= 40`) comes after character 1,200 returns that line,
        and the reported range covers it;
      - a chunk with no query term returns the head with its own range;
      - a chunk under the limit is unchanged.

## 6. The answer-check suite

- [x] 6.1 Add `AnswerCheckSuite` with its dataset loader, `answer-check` in the `all` list, a Makefile target
      `eval-answer-check` with a `##` help line, and `answer-check` in `make eval`'s SUITE list. Progress is one
      `answer-check i/N id: ok|WRONG` line per case through the harness's progress. The suite refuses to run without
      `JEV_MAF_LAB`, with the same message style as the guardrail suite. Verify with a unit test on FakeJev: a row
      expected unsupported at 0.1 is counted correct, a row at 0.35 is counted in the band, a timeout is counted
      unchecked with its reason, and no chat client is resolved.

## 7. Measure and decide (paid runs, needs `JEV_MAF_LAB`; run as a delegated measurement)

- [x] 7.1 Run `make eval-guardrail` twice. Read the design split first and keep 0.85 or move `ContentWithholdAt` for
      codebase items only as D9 says, then read the holdout blind. Verify that the report shows 0 new billing false
      positives against the baseline, codebase benign let-through ≥ the design target, and the to-AI-only planted row
      named as the known miss. Check the unscreened count before accepting anything.
- [ ] 7.2 Run `make eval-answer-check` twice. Choose `NotGroundedAt`/`GroundedPassAt` (and the relevance pair) on the
      design split by D9's rule and confirm on the holdout, per language (en, bg, bg-latn) and per domain. Verify that
      the report shows the reviewed good codebase answers raising no signal and the reviewed wrong ones flagged, and
      the billing rows no worse than today's single floor.
      **Open:** both runs done (DECISIONS §63); the band was set to 0.3/0.5 (grounded) and 0.2/0.8 (relevant) by D9's rule on those probabilities, but not re-run at it, and two unsupported rows fall in the band rather than being flagged. Run `make eval-answer-check` once at the new defaults to confirm.
- [ ] 7.3 Run `make eval SUITE=generation` twice and `make eval SUITE=selection` once. Verify that the codebase and
      Bulgarian cases report their checks, and that selection is unchanged (the snippet window must not move tool
      choice).
      **Open:** needs mcp-code rebuilt with the snippet window, i.e. `make` (restarts the stack — not allowed from this worktree), then `make eval SUITE=generation` twice and `make eval SUITE=selection` once.
- [ ] 7.4 Add a DECISIONS.md section with the guard's codebase numbers, the record-only rule and its known miss, the
      calibrated band superseding §42's provisional floors, the rate of `sources over cap`, and the rollback levers.
      Note in it that no model or package moved. Accept the baselines (`make eval-accept`) only after reading the runs.
      Verify that the section exists and that `evals/baseline.json` changes only in the suites that were read.
      **Open:** the DECISIONS §63 section is written; the baselines are not accepted. After 7.2's confirming run: `make eval-accept SUITE=guardrail` and `make eval-accept SUITE=answer-check`, then check that `evals/baseline.json` changed only in those suites.

## 8. Jev review

- [x] 8.1 Walk the jev-usage §7 checklist for R1 and R2 and record each answer in the DECISIONS section:
      - closed, atomic Nouls;
      - nothing code could compute (dedupe, citation match, cap, normalisation, window and band are code);
      - one request per state;
      - a minimal state with backticked fields;
      - positive polarity with aligned criteria;
      - risk-scaled thresholds with a review band on R2;
      - fallback;
      - Jev not the security boundary;
      - the model pinned and logged;
      - 429/529 retries on the shared client;
      - labelled en/bg/bg-latn inputs.
      Verify that every box is ticked or has a written reason.
- [x] 8.2 Check that no log line added or changed carries a question, answer, snippet or path list, and that
      `JEV_MAF_LAB` appears only in the bearer header. Verify with the existing log-content tests extended to a codebase
      turn (`AnswerCheckTests`, `GuardrailTests`), and with `make test` and `make lint` green.

## 9. Documentation

- [x] 9.1 Update `docs/trace-events.md`:
      - the `guardrail` row: the codebase context, `recordOnly`, the withheld stub;
      - the `answer.check` row: dedupe and cited-first ordering, `sources over cap`, `context`, `relevantPassAt`,
        `groundedPassAt`, `duplicates`, the `uncertain` verdict and the new title.
      Verify by reading the rows against the event the code writes.
- [x] 9.2 Update `docs/http-api.md` with `JevStatsReport.answerCheck.uncertain` (optional). Verify it matches
      `JevStatsContracts`.
- [x] 9.3 Check `README.md` prose that lists eval suites or describes the guard and answer check outside `generated:`
      blocks, and update it by hand where it is untrue. Never edit inside a `generated:` block. Verify with a grep for
      `answer check` and `eval-guardrail` in README prose.
- [x] 9.4 Run `make docs` to rewrite the generated blocks (the Makefile help table gains `eval-answer-check`), then
      run `make docs-check` and verify it passes.
