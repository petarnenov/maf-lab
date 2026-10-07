# The billing plugin's make targets (extract-billing), read by the core Makefile (-include plugins/*/plugin.mk) and joined
# to its targets as extra prerequisites — GNU make's multiple rules per target: `make index`, `make graph`, `make reindex`,
# `make drift`, `make rebuild-index`, `make migrate` and the first `make` (index-if-empty) cover billing while this folder
# exists. Progress and stopping are the indexer's own: its bar, and Ctrl+C at a safe point with exit 130.

# Billing's seeds live in this folder: exported, so every host-side run make starts (dev, test, index, graph, eval) finds
# them; compose sets them from files/billing.env. `?=` keeps a value from the environment.
export Billing__SeedPath ?= $(ROOT)/plugins/billing/files/seed/billing-runs.json
export Billing__AccountsSeedPath ?= $(ROOT)/plugins/billing/files/seed/billing-accounts.json

# Billing's corpus lives in this folder too; its collection is maf_chunks.
BILLING_ENV = Indexing__CorpusRoot=$(ROOT)/plugins/billing/files/corpus Qdrant__Collection=maf_chunks Qdrant__MetaCollection=maf_meta

.PHONY: index-billing graph-billing reindex-billing drift-billing rebuild-index-billing migrate-billing index-billing-if-empty \
        neo4j-chunks eval-retrieval-backends

index-billing: require-dotnet infra indexer ## Index the billing corpus (→ maf_chunks, served by mcp-retrieval) only; unchanged documents are skipped
	$(HOST_ENV) $(BILLING_ENV) $(INDEXER) index

graph-billing: require-dotnet infra indexer ## Build the billing graph only (accounts, runs, households) in Neo4j
	$(HOST_ENV) $(BILLING_ENV) $(INDEXER) graph --only billing

reindex-billing: require-dotnet infra indexer
	$(HOST_ENV) $(BILLING_ENV) $(INDEXER) index --force

drift-billing: require-dotnet infra indexer
	$(HOST_ENV) $(BILLING_ENV) $(INDEXER) drift

rebuild-index-billing: require-dotnet infra indexer
	$(HOST_ENV) $(BILLING_ENV) $(INDEXER) rebuild $(if $(filter 1,$(FORCE)),--yes,)

migrate-billing: require-dotnet infra indexer
	$(HOST_ENV) $(BILLING_ENV) $(INDEXER) migrate --to $(TO)

# Billing's collection and graph, only when each is empty (the first `make`).
index-billing-if-empty: require-dotnet
	@$(HOST_ENV) $(BILLING_ENV) scripts/index_if_empty.sh --source billing

neo4j-chunks: require-dotnet infra indexer ## Spike: copy each domain's chunks from Qdrant into Neo4j for eval-retrieval-backends (billing's here; portfolio adds its own)
	$(HOST_ENV) $(BILLING_ENV) $(INDEXER) neo4j-chunks
	@printf 'store size: neo4j %s · qdrant %s\n' "$$($(COMPOSE) exec -T neo4j du -sh /data/databases/neo4j 2>/dev/null | cut -f1)" "$$($(COMPOSE) exec -T qdrant du -sh /qdrant/storage/collections 2>/dev/null | cut -f1)"

eval-retrieval-backends: require-dotnet ## Spike comparison: retrieval cases on Qdrant and on Neo4j side by side, never gated (run make neo4j-chunks first)
	$(EVAL) retrieval-backends

index: index-billing graph-billing
graph: graph-billing
reindex: reindex-billing
drift: drift-billing
rebuild-index: rebuild-index-billing
migrate: migrate-billing
index-if-empty: index-billing-if-empty
