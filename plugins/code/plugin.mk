# The code plugin's make targets (introduce-plugins 5.2), read by the core Makefile (-include plugins/*/plugin.mk) and
# joined to its targets as extra prerequisites — GNU make's multiple rules per target: `make index`, `make graph`,
# `make reindex` and the first `make` (index-if-empty) cover the codebase while this folder exists. Progress and stopping
# are the indexer's own: its bar, and Ctrl+C at a safe point with exit 130.

# The codebase is indexed from the repository itself, by structure, into its own collection: chunks sized in embedding
# tokens (well under embeddinggemma's 2048), BM25 over identifiers split into their words (add-codebase-search).
CODE_ENV = Indexing__Layout=repository Indexing__CorpusRoot=$(ROOT) Indexing__MaxChunkTokens=1024 Indexing__Bm25Tokenizer=code \
           Qdrant__Collection=maf_code_chunks Qdrant__MetaCollection=maf_code_meta

.PHONY: index-code graph-code reindex-code index-code-if-empty

index-code: require-dotnet infra indexer ## Index the repository itself (→ maf_code_chunks, served by mcp-code) only; unchanged files are skipped
	$(HOST_ENV) $(CODE_ENV) $(INDEXER) index

graph-code: require-dotnet infra indexer ## Build the code graph only (calls, types, tests) in Neo4j
	$(HOST_ENV) $(INDEXER) graph --only code

reindex-code: require-dotnet infra indexer
	$(HOST_ENV) $(CODE_ENV) $(INDEXER) index --force

# The codebase's collection and graph, only when each is empty (the first `make`).
index-code-if-empty: require-dotnet
	@$(HOST_ENV) $(CODE_ENV) scripts/index_if_empty.sh --source code

index: index-code graph-code
graph: graph-code
reindex: reindex-code
index-if-empty: index-code-if-empty
