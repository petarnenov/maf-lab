# Tasks

Jev: task 1.1 moves every Jev call behind `IDecisionEngine`. Read docs/rules/jev-usage.md before it. The questions
themselves do not change.

Depends on `introduce-plugins`.

- [x] 1.1 Add `IDecisionEngine`. The Jev client becomes the `jev` provider plugin. Exactly one engine must be installed,
      or startup fails. A contract suite runs for every engine. Record input tokens per turn by domain count. Verify
      that `make eval SUITE=selection` holds the accepted baseline through the abstraction.
      Done at the code level (DECISIONS §87); the golden request bodies recorded from main (this change's first two
      commits) stay byte-identical through the real engine. Rebased onto 529c0cb: with the plugin present make
      test-dotnet 1923, vitest 702, docs-check in sync, validate --strict valid, lint clean; with it moved aside (after
      make docs) test-dotnet 1874 (2 skipped: the eval CLI tests need a bundled engine), vitest 702, docs-check in sync,
      validate valid. ci-e2e-core (the decline with only the core's providers, AG-UI 8/8) and ci-e2e (verify, A2A every
      scenario, AG-UI 8/8, test generation) green. `make eval SUITE=selection` through the jev engine (preflight
      `--limit 3` metered 29,625 input tokens; then one full run, 20261007-171311): exactMatch 0.980, precision 0.982,
      recall 1, negativeAccuracy 1 against the accepted 0.959 / 0.964 / 1 / 1 (not accepted as a new baseline);
      420,156 decision-engine input tokens.
- [x] 1.2 Add the chat-model and embeddings providers (`ollama-cloud`, `ollama-embeddings`) over `IChatClient` and
      `IEmbeddingGenerator`, with `MAF_CHAT_MODEL` and `MAF_CORE_PROVIDERS`. Verify that every embeddings request still
      carries its instance's `num_thread` through contract tests. The live eval gate is task 1.5, deferred to the end
      of the agreed code migration by the user's decision on 2026-10-08.
      Implemented 2026-10-08: named chat factory and purpose-specific MEAI embedding adapter in provider lib projects;
      startup selection validation, default core providers, query/batch threads and truncation refusal preserved,
      cancellation carried through both interfaces. All 1933 .NET tests (unit + integration), Vitest 702,
      warnings-as-errors build, docs-check (126 Python tests) and all 62 strict OpenSpec items passed.
      Real eval started after approval, 20261008-190507: intent (accuracy 0.9901), answer-check (accuracy 0.9231)
      and code-route (accuracy 0.9206) passed. Guardrail regressed on benignPass:content:verdict (1 → 0.8889),
      generation-judge on points contradictedAccuracy:bg (0.9784 → 0.9459) and pointAccuracy:bg (0.9513 → 0.9189).
      One confirming recheck (20261008-191436) passed guardrail and generation-judge with no code or baseline changes.
      Thus all five index-independent suites now have passing baseline comparisons.
      The other seven suites are deferred until the entire planned code migration is complete (task 1.5).
      At the user's request, indexing was stopped at a safe point after 591/624 billing documents; the remaining
      index/graph steps did not start. No baseline has been changed.
- [x] 1.3 `make core` installs `MAF_CORE_PROVIDERS` and no domain. Verify the core-only CI leg still declines every
      turn.
      Verified 2026-10-08: make ci-e2e-core passed with exactly the three default providers and no domain,
      including the English/Cyrillic decline, empty stub request journals and AG-UI conformance.
- [x] 1.4 Update CLAUDE.md, project.md, `docs/plugins.md` and DECISIONS §87.
      Also updated README, generated project context, test-agent image wiring and model discovery.
      make docs-check passed (126 Python tests; documentation in sync); strict change validation passed.
- [ ] 1.5 Deferred final validation: after the entire agreed code migration is complete, update the indexes and
      graph, then run the remaining live eval suites (selection, retrieval, generation, injection, confirmation,
      domain, presentation) and compare with the accepted baselines. Apply the stage/prod promotion gate only when
      this validation holds; leave this task open until then. Do not block the remaining code migrations on it.
