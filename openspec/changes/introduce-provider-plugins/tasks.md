# Tasks

Jev: task 1.1 moves every Jev call behind `IDecisionEngine`. Read docs/rules/jev-usage.md before it. The questions
themselves do not change.

Depends on `introduce-plugins`.

- [ ] 1.1 Add `IDecisionEngine`. The Jev client becomes the `jev` provider plugin. Exactly one engine must be installed,
      or startup fails. A contract suite runs for every engine. Record input tokens per turn by domain count. Verify
      that `make eval SUITE=selection` holds the accepted baseline through the abstraction.
      Done at the code level (DECISIONS §86); the golden request bodies recorded from main (this change's first two
      commits) stay byte-identical through the real engine. Rebased onto 529c0cb: with the plugin present make
      test-dotnet 1923, vitest 702, docs-check in sync, validate --strict valid, lint clean; with it moved aside (after
      make docs) test-dotnet 1874 (2 skipped: the eval CLI tests need a bundled engine), vitest 702, docs-check in sync,
      validate valid. ci-e2e-core (the decline with only the core's providers, AG-UI 8/8) and ci-e2e (verify, A2A every
      scenario, AG-UI 8/8, test generation) green. Open: the one confirming `make eval SUITE=selection`.
- [ ] 1.2 Add the chat-model and embeddings providers (`ollama-cloud`, `ollama-embeddings`) over `IChatClient` and
      `IEmbeddingGenerator`, with `MAF_CHAT_MODEL` and `MAF_CORE_PROVIDERS`. Verify that every embeddings request still
      carries its instance's `num_thread`, and that the `make eval` baselines hold.
- [ ] 1.3 `make core` installs `MAF_CORE_PROVIDERS` and no domain. Verify the core-only CI leg still declines every
      turn.
- [ ] 1.4 Update CLAUDE.md, project.md, `docs/plugins.md` and DECISIONS §86.
