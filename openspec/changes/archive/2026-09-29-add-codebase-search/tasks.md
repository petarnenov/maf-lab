# Tasks

## 1. Embedding window and refusal

- [x] 1.1 Add `EmbeddingProfile.MaxInputTokens` (2048 for `dense_v3`/embeddinggemma). Make `DenseEncoder` embed
  documents through Ollama's native call with `truncate: false` when a window is known, and map a refusal to
  `InputTooLongException`. Verify: a unit test for the fallback path with a fake generator, and a live probe that
  over-long input is refused.

## 2. Chunking

- [x] 2.1 `ChunkBudget` (characters or tokens; `int` converts) and `TokenEstimator`, with `ChunkText.SplitToFit` taking a
  budget. Verify: unit tests showing that the estimate over-counts a sample of real repository text, and that a
  token-budget split keeps every piece within the budget.
- [x] 2.2 `CodeChunker(structural)`: leading comments, attributes and decorators; the header gives back its first
  member's comment; unclaimed runs grouped per type or file. Verify: unit tests for a doc comment kept, a property not
  lost, and non-structural output unchanged.
- [x] 2.3 `ChunkBuilder`: section paths qualified for repository files, the token ceiling split, and 1-based line spans.
  Verify: unit tests for the ceiling and the line spans. The existing eval chunk-id test still passes, so billing and
  portfolio ids are unchanged.

## 3. Corpus and vocabulary

- [x] 3.1 `RepositoryCorpusLoader` (git ls-files, include/exclude prefixes, build output skipped, size limit rejected,
  all shared) and `IndexingOptions` (Layout, MaxChunkTokens, RepositoryInclude/Exclude, RepositoryMaxFileBytes,
  Bm25Tokenizer). Verify: unit tests on a temp folder for included and excluded paths and the rejected size.
- [x] 3.2 `Bm25Model.Tokenizer` and `Bm25Tokenizer.TokenizeCode`, with encoder and search tokenizing through the model.
  Verify: unit tests for identifier parts, for an old serialized model reading as `words`, and for the query matching an
  identifier.
- [x] 3.3 `ChunkRecord.StartLine`/`EndLine` in the payload, and the pipeline wiring (the corpus by layout, the budget,
  the ceiling, the tokenizer, lines). Add `chunks --tokens`. Verify: `chunks --tokens` on the repository reports the
  maximum under the ceiling.

## 4. Codebase MCP server

- [x] 4.1 `Maf.Lab.Domain/Code` DTOs, and the `Maf.Lab.CodeSearch` project (Program, BootstrapService,
  `codesearch.json`, Dockerfile, launch settings, solution entry). Verify: `dotnet build` of the solution.
- [x] 4.2 `CodeSearchService` and `CodeSearchTools` (`search_codebase`, `ask_codebase`): the kind and path filters, the
  snippet shape, the grounded prompt, and no model call without snippets. Verify: unit tests for the tool list and
  annotations, the path filter, a prompt that treats snippets as data, and an ungrounded answer without a model call.

## 5. Topology and make

- [x] 5.1 Compose `mcp-code` (plus the CI override), the nginx `/code/mcp` upstream and location, and `lb` depending on
  it. Verify: `docker compose config` is valid and nginx `-t` passes.
- [x] 5.2 Makefile `index-code` and `CODE_ENV`; `index`, `reindex` and `index-if-empty` include the codebase; `make dev`
  runs it on :5092. Verify: `make index-code` completes against the running stack, and a second run reports all
  documents unchanged.

## 6. Api and web

- [x] 6.1 Api `POST /api/code/snippets`: an MCP client to `CodeSearch:Endpoint` with the user's token, returning
  `CodeSearchResult`, or 503 without a host. Compose and dev configuration. Verify: api tests with a fake code-search
  client for the happy path, 503 and 401.
- [x] 6.2 Web: pane tabs (Behind the scenes | Code snippets) in the chat's right pane, and `CodeSnippetsPanel`
  (per-file groups, numbered lines, loading, empty and error states, fetched only while shown). Verify: Vitest tests
  for the default tab, the fetch on open, grouping, a changed question and the error state.

## 7. Verification

- [x] 7.1 Jev review checklist (docs/rules/jev-usage.md §7). This change adds no Jev request; confirm that the gate
  reused by `mcp-code` falls back open. Smoke-test labelled codebase questions in English and Bulgarian against the
  running server: each expected file appears in the top 5, and an off-topic question returns nothing.
- [x] 7.2 `make test` and `make lint` pass. Rebuild the stack with `make up`, and check `/code/mcp` and the Code
  snippets tab in the browser.
- [x] 7.3 DECISIONS.md section for the codebase corpus: the estimator, refusal over truncation, the budget and ceiling,
  and the code tokenizer. Update the layout in `openspec/project.md` and `openspec/config.yaml` with
  `Maf.Lab.CodeSearch`.
