# Tasks

## 1. Domain and Jev

- [x] 1.1 `Domains.Codebase` (in `All`, and `SearchTool` → `search_codebase`). Add the `in_codebase` Noul with its
  `JevDomainInstructions` in `JevIntentClassifier` (same request). Update the `OutOfScope` replies. Verify: unit tests
  with `FakeJev` for the codebase in scope, a general-programming question as none, Jev down meaning no verdict, and
  the request body carrying `in_codebase` beside the others.
- [x] 1.2 Forcing: a codebase-primary question forces `search_codebase` for any intent but chitchat, and a crossing
  procedural question forces both searches. Verify: `ForcedSearches` unit tests.

## 2. Tools by conversation domains

- [x] 2.1 `IToolSource.GetToolsAsync(..., domains)`, `McpServerOptions.Tools` allow-list, and a turn that does not
  select billing not failing on billing. Verify: unit tests with fake servers for the selected domains only, the
  allow-list, and null loading everything.
- [x] 2.2 `ConversationRow.Domains` and `ChatTurnRunner.SelectDomains` (in scope / conversation / all). Store on
  in-scope turns, and add `loaded`, `loadReason` and `storedDomains` to the `domain` trace event. Verify: api tests for
  a code turn offering only `search_codebase`, a follow-up keeping the portfolio tools, and no verdict loading every
  server.

## 3. Sources and prompt

- [x] 3.1 `SourceRef` code fields, and one reader (`SourceRef.FromSearchItem`) used by sources, the answer-check reading and history.
  The review queue skips code searches. Verify: a chat test where a `search_codebase` result yields code sources with
  lines, a history round trip, and the guard screening code snippets.
- [x] 3.2 `Prompts/system.v4.md` (Codebase tools, scope, citation rule) as the default. Verify: the `SystemPromptTests`
  updates pass.
- [x] 3.3 Configuration: api `Agent__Servers__1__*` in compose, the dev default in appsettings, and `make dev`. Verify:
  `docker compose config`, and a live turn in the running stack.

## 4. Web

- [x] 4.1 Code snippets tab: used-by-answer mode from the turn's code sources (no fetch) vs related-code mode;
  count badge; switching by itself once per streaming turn. Verify: Vitest tests for each mode, the badge, the auto
  switch firing once, and a manual switch standing.
- [x] 4.2 `SourcesPanel` code sources open the tab and highlight the snippet. Add the tool labels and the codebase
  colour in the Domains view. Verify: Vitest tests.

## 5. Evals

- [x] 5.1 Domain labels as sets (`billing`, `portfolio`, `codebase`, joined multi-labels, legacy `both`). Add codebase
  and general-programming cases (en, bg, bg-latn) to `domain.jsonl`, codebase cases to `selection.jsonl`, and the
  code server to the eval host and Makefile `EVAL_HOST`. Verify: dataset loader tests pass.
- [x] 5.2 Jev review checklist (docs/rules/jev-usage.md §7). Run the `domain`, `intent` and `selection` suites
  against the running stack, as a subagent, and compare with the last baselines. Record the result in DECISIONS.md.

## 6. Verification

- [x] 6.1 `make test` and `make lint` pass. `make up`. In the browser, the screenshot's questions ("как в кода се
  прави идемпотентност на тул", "покажи ми дефиницията на code mcp сървъра") get grounded chat answers with the Code
  snippets tab showing the used snippets.
- [x] 6.2 DECISIONS.md section. Update README (codebase questions in the chat).
