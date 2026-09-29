# Proposal

## Why

The lab can explain fee billing and portfolios, but nothing answers questions about the lab itself: where the tenant
filter is applied, how a chunk is sized, which decision set the relevance floor. Those answers are spread over about 480
files of C#, TypeScript, specs and decisions. The lab exists to exercise RAG end to end, so its own codebase is the
natural next corpus. Code needs what prose does not: lexical matches on identifiers, chunks cut at symbols, and places
(file and lines) rather than headings.

Chunk size also has a hard limit. embeddinggemma reads at most 2048 tokens (Ollama reports `gemma3.context_length`
2048), and by default Ollama cuts longer input **silently**. A vector would then stand for text the model never read.
The billing corpus never came near that limit (1500-character chunks). A source file does.

## What Changes

- **Codebase corpus.** The indexer gains a `repository` layout. It reads the files git tracks under configured prefixes
  (`src/`, `web/src/`, `tests/`, `tools/`, `scripts/`, `openspec/specs/`, `docs/`, and the root READMEs and decisions).
  Source files become `code`; Markdown becomes `docs`. Every document is `shared`: the codebase belongs to no firm.
  It goes into its own collection, `maf_code_chunks`, with its own BM25 vocabulary, `maf_code_meta`.
- **Structural chunking.**
  - Repository code is cut per type and member.
  - Each symbol takes the doc comments, attributes and decorators written directly above it.
  - Lines no symbol claims (properties, one-line records, fields, usings, top-level code) become chunks of their
    enclosing type or file. They are no longer dropped.
  - Section paths start with the path from the repository root.
  - Every chunk records its 1-based line span.
  - Billing and portfolio chunks are cut exactly as before.
- **Chunks sized in embedding tokens.**
  - A conservative token estimator, calibrated against embeddinggemma's own count, sizes repository chunks to a
    budget of 1024 tokens.
  - A hard ceiling applies to every corpus: the model's window less the document prefix (and room for a contextual
    sentence). A chunk over the ceiling is split again.
  - Embedding profiles state `MaxInputTokens` (2048 for embeddinggemma).
  - With a known window, document embedding asks Ollama to **refuse** input it would cut (`truncate: false`). A
    chunk that is too long fails the index run instead of being cut silently.
- **Lexical search over identifiers.** The BM25 vocabulary records its tokenizer. The new `code` tokenizer keeps each
  identifier and also its camelCase / PascalCase / acronym parts, so "tenant scoped search" meets
  `TenantScopedSearch`. Existing vocabularies keep the `words` tokenizer.
- **A new MCP server, `mcp-code`** (`Maf.Lab.CodeSearch`), behind the balancer at `/code/mcp`, authenticated with the
  same user token:
  - `search_codebase` returns snippets with path, line range, symbol, section, kind and language. Optional filters:
    kind (code/docs) and path prefix.
  - `ask_codebase` answers a question with the chat model, only from the retrieved snippets, citing `path:start-end`.
    It says so when nothing relevant was found.
  - Ranking is the existing hybrid search (dense + BM25, RRF, relevance gate, reranker) through the one tenant-scoped
    query method.
- **The chat's right pane gets two tabs**:
  - **Behind the scenes** is the monitor as it is today.
  - **Code snippets** shows the files and lines of the repository that match the selected turn's question. They are
    grouped per file, with line numbers. They come from a new api endpoint that calls `search_codebase` as the user.
- **Make and topology.** `make index-code` indexes the repository. `make index`, `make reindex` and `index-if-empty`
  include it. Compose runs `mcp-code`, nginx routes `/code/mcp`, and `make dev` runs it on :5092.

No Jev call is added or changed. `mcp-code` reuses the retrieval core's existing relevance judge unchanged.

## Capabilities

### New Capabilities
- `codebase-search`: the codebase MCP server, its corpus, its two tools, and the api endpoint that serves code snippets
  to the web.

### Modified Capabilities
- `document-indexing`: the repository layout, structural code chunking with line spans, token-sized chunks under the
  embedding model's window, refusal of silent truncation, and the BM25 tokenizer recorded with the vocabulary.
- `web-ui`: the right pane's two tabs, Behind the scenes and Code snippets.
- `load-balancing`: `/code/mcp` routes to the code server's pool.
- `make-workflow`: `make index-code`, and the codebase included in `make index`, `reindex` and `index-if-empty`.

## Impact

- New project `src/Maf.Lab.CodeSearch` (Program, tools, service, Dockerfile, `codesearch.json`). Compose service
  `mcp-code`, nginx upstream and location, CI override, `make dev`.
- `Maf.Lab.Retrieval`:
  - `EmbeddingProfile.MaxInputTokens`;
  - `DenseEncoder` uses Ollama's native embed call with `truncate: false` for documents;
  - `Bm25Model.Tokenizer` and `Bm25Tokenizer.TokenizeCode`;
  - `ChunkRecord.StartLine` / `EndLine` in the payload.
- `Maf.Lab.Indexing`:
  - `ChunkBudget` and `TokenEstimator`;
  - `CodeChunker(structural)`;
  - `RepositoryCorpusLoader`;
  - `IndexingOptions` (Layout, MaxChunkTokens, RepositoryInclude/Exclude, Bm25Tokenizer);
  - the ceiling in `ChunkBuilder` and `IndexingPipeline`;
  - `chunks --tokens`.
- `Maf.Lab.Domain`: `Code/CodeDtos.cs`.
- `Maf.Lab.Api`: a code-snippets endpoint and an MCP client for `mcp-code` (`CodeSearch:Endpoint`).
- `web/`: a pane-level tab bar around the monitor, and a `CodeSnippetsPanel`.
- The billing and portfolio indexes are unchanged: same chunk ids, same text, same `words` vocabulary. No eval rerun is
  required for them. No package version moves.
