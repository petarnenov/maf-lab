# Design

## Context

- The indexer reads one layout, `{tenant}/{docs|procedures|code}/...`, and cuts chunks to 1500 characters.
- `CodeChunker` already splits brace languages per type and member. It drops every line no symbol claims, which covers
  properties, fields, one-line records and usings. It also drops the doc comments and attributes above members.
- The portfolio domain set the pattern for another corpus: its own collection and vocabulary, and its own MCP server
  that reuses the retrieval core as a library (`AddMafRetrievalCore`), with collections pinned in configuration.
- Measured on the running stack (Ollama `api/show`, `api/embed`):
  - embeddinggemma's window is 2048 tokens.
  - With the default `truncate: true`, a longer input comes back with `prompt_eval_count` 2048 and no error. With
    `truncate: false` it is refused (400, "the input length exceeds the context length").
  - C# runs about 3.9 characters a token and Bulgarian prose about 3.2.
- OllamaSharp 5.4.30 ignores `EmbeddingGenerationOptions.RawRepresentationFactory` for embeddings (probed: an over-long
  input was still cut). Its native `IOllamaApiClient.EmbedAsync(EmbedRequest { Truncate = false })` refuses the input.

## Goals / Non-Goals

**Goals:**
- No chunk of any corpus is ever embedded cut.
- Code is found both by identifier and by meaning, and returned as a place in a file.
- The billing and portfolio indexes stay byte-for-byte what they were, so their evals do not move.

**Non-Goals:**
- The billing chat agent does not get the codebase tools, and Jev gets no "code" domain. The Code snippets tab calls
  the code search directly. Routing chat turns to code is a later change with its own selection eval.
- No retrieval eval suite for the codebase yet. Floors and the gate use the existing defaults (see Risks).
- No live re-indexing on file change. `make index-code` is incremental by content hash.

## Decisions

### Token estimate instead of a tokenizer
- Chunks are sized with `TokenEstimator`, a character-class heuristic:
  - ASCII letter runs ⌈n/4⌉;
  - other letter runs ⌈n/2⌉;
  - a single space 0;
  - an indentation run 1;
  - any other character 1, or 2 for other non-ASCII.
- It was calibrated against embeddinggemma's own count on 160 random windows of this repository. It over-counted every
  one: by 1.13× at minimum and 1.39× at the median.
- Alternative: call Ollama to count tokens per chunk. That doubles the embedding calls, ties chunking to one provider,
  and makes `chunks` need a running model.
- Alternative: ship the Gemma SentencePiece model in .NET. That is a new dependency for a number that only needs to be
  an upper bound.
- The estimate is the sizing tool. The provider's refusal (below) is the guarantee.

### Refuse, don't truncate
- When the profile states `MaxInputTokens`, `DenseEncoder.EmbedDocumentsAsync` calls OllamaSharp's native `EmbedAsync`
  with `Truncate = false`. A refusal becomes `InputTooLongException` and the run fails.
- Queries keep the M.E.AI path with truncation: a cut query still searches.
- Other providers (OpenAI) keep the M.E.AI path. It is behind the same `IDenseEncoder` seam, so tests are unaffected.
- The native call returns the same vectors (same `/api/embed` endpoint and model). The billing corpus's vectors are
  therefore unchanged.

### Budget vs ceiling
- The repository corpus uses a **budget** of 1024 estimated tokens (≈740 real). That keeps a method whole while a
  chunk still means one thing to the dense model.
- Every corpus is held under a **ceiling**:
  `min(MaxInputTokens) − estimate(DocumentPrefix) − (contextual ? 160 : 0)`, which is 2041 for embeddinggemma. The
  ceiling is checked on `SectionPath + "\n" + Text`, the text actually embedded.
- A billing chunk of 1500 characters never reaches the ceiling, so billing ids do not change. The chunk-id eval test
  guards that.
- `ChunkBudget` converts implicitly from `int`, so every character-budget caller and test is unchanged.

Observed on the repository: 479 files make 4881 chunks. Their estimates are median 172, p95 842 and max 1075 (section
path included), against a ceiling of 2041.

### Structural mode is opt-in
- `CodeChunker(structural: true)` is used only for documents from the repository layout (`SourceDocument.FromRepository`).
- It adds four things:
  - leading trivia: `//`, `///`, `/*`, `*`, `[attr]` and `@decorator` lines directly above a symbol;
  - a type header gives up trailing trivia to its first member;
  - unclaimed runs grouped per innermost type or `(file)`;
  - no fallback to brace parsing for non-brace files (`.sh`), which get windows.
- Keeping it opt-in keeps billing chunks identical.
- Line spans are computed in `ChunkBuilder` for every chunker, by placing each chunk's text back in the file with a
  forward cursor, or by its first line when a chunker re-joined blocks. So Markdown gets lines too, without touching
  its chunker.

### Tokenizer travels with the vocabulary
- `Bm25Model.Tokenizer` (default `words`) is serialized with the model, and encode and query both call
  `model.Tokenize`. A corpus cannot be indexed with one tokenizer and queried with another.
- The `code` tokenizer emits the whole identifier and then its parts. The whole name keeps an exact-identifier query
  ranking above the parts, through IDF on the rare full token.
- Switching tokenizer clears the term ids; the index run already rebuilds statistics from the whole corpus.

### A separate server, not a tool on mcp-retrieval
- `Maf.Lab.CodeSearch` mirrors `Maf.Lab.Portfolio`: collections pinned last in configuration, the core reused, and
  `DocumentSearchService.RankAsync` for ranking. No new query builder: the one tenant-scoped method applies the filter,
  and the codebase is `shared`.
- The path-prefix filter is applied to a larger ranked list (60), not in Qdrant. A prefix match would need a new filter
  condition in the one query method, for a convenience filter.
- `ask_codebase` uses the configured chat model (`gpt-oss:120b` via `IChatClientFactory`). The snippets are wrapped in
  `<snippet place=…>` tags, stated to be data, and `</snippet>` inside a text is defused. With no snippets the model is
  not called.

### Web: pane tabs above the monitor, api endpoint as the bridge
- `ChatPage`'s right `aside` gets a two-tab bar. **Behind the scenes** renders `MonitorPanel` unchanged. **Code
  snippets** renders `CodeSnippetsPanel` for the question of the turn the monitor follows.
- The panel uses TanStack Query keyed by the question, and is enabled only while its tab is shown.
- The browser cannot call MCP, so the api gets `POST /api/code/snippets { question, maxResults? }`. It opens an MCP
  client to `CodeSearch:Endpoint` (compose: `http://lb/code/mcp`, dev: `http://localhost:5092/mcp`) with the user's
  bearer token, the same way `McpToolSource` does, and returns the structured `CodeSearchResult`.
- An unreachable server gives 503 with a host-free message.

## Risks / Trade-offs

- [The relevance gate's floor (0.3) and embeddinggemma's dense floor (0.22) were calibrated on billing prose, not code]
  → Both are the core defaults and configurable per server (`Retrieval__*` on `mcp-code`). A smoke set of code
  questions is checked by hand in this change. A codebase retrieval eval is listed as a follow-up.
- [Query translation sends non-English questions through the chat model first] → The same trade-off as the other
  corpora. Identifiers survive translation in practice. A failed translation searches the original.
- [First index of ~4.9k chunks takes minutes on local Ollama (≈3 chunks/s measured)] → Incremental by content hash
  afterwards. `index-if-empty` runs it once.
- [The estimator could under-count an exotic file] → The provider refuses, and the run fails naming the window. It
  never stores a cut vector.
- [The codebase is visible to every authenticated user] → By design: it is shared and contains no tenant data. The
  corpus is only what the include prefixes list, and `.env`-style files are hidden paths or not source extensions.

## Migration Plan

- New collections only. Deploy: `make up`, then `make index-code` (or plain `make`, which indexes when empty).
- Rollback: remove the `mcp-code` service and route, and drop `maf_code_chunks` / `maf_code_meta`.
- The billing and portfolio collections need no action. Their vocabularies load as `words`, as before.
