# The portfolio plugin's make targets (extract-portfolio), read by the core Makefile (-include plugins/*/plugin.mk) and
# joined to its targets as extra prerequisites — GNU make's multiple rules per target: `make index`, `make reindex` and
# the first `make` (index-if-empty) cover portfolio while this folder exists. Progress and stopping are the indexer's
# own: its bar, and Ctrl+C at a safe point with exit 130.

# Portfolio's seed lives in this folder: exported, so every host-side run make starts (dev, test, eval) finds it; compose
# mounts it for the server. `?=` keeps a value from the environment.
export Portfolio__SeedPath ?= $(ROOT)/plugins/portfolio/files/seed/portfolio-households.json

# Its corpus is indexed by the same indexer into its own collection and BM25 vocabulary.
PORTFOLIO_ENV = Indexing__CorpusRoot=$(ROOT)/plugins/portfolio/files/corpus Qdrant__Collection=maf_portfolio_chunks \
                Qdrant__MetaCollection=maf_portfolio_meta

.PHONY: index-portfolio reindex-portfolio index-portfolio-if-empty neo4j-chunks-portfolio

index-portfolio: require-dotnet infra indexer ## Index the portfolio corpus (→ maf_portfolio_chunks, served by mcp-portfolio) only
	$(HOST_ENV) $(PORTFOLIO_ENV) $(INDEXER) index

reindex-portfolio: require-dotnet infra indexer
	$(HOST_ENV) $(PORTFOLIO_ENV) $(INDEXER) index --force

# Portfolio's collection, only when it is empty (the first `make`); it has no graph of its own.
index-portfolio-if-empty: require-dotnet
	@$(HOST_ENV) $(PORTFOLIO_ENV) scripts/index_if_empty.sh --source portfolio --no-graph

# The retrieval-backends spike's copy of portfolio's chunks into Neo4j (make neo4j-chunks).
neo4j-chunks-portfolio: require-dotnet infra indexer
	$(HOST_ENV) $(PORTFOLIO_ENV) $(INDEXER) neo4j-chunks

index: index-portfolio
reindex: reindex-portfolio
index-if-empty: index-portfolio-if-empty
neo4j-chunks: neo4j-chunks-portfolio
