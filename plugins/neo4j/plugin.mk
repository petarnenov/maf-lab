# The graph store's make targets (extract-billing), joined to the core's by GNU make's multiple rules per target: the
# host-side indexer's infrastructure includes Neo4j while this folder exists.

.PHONY: infra-neo4j

infra-neo4j: require-docker
	@$(COMPOSE) up -d --wait neo4j

infra: infra-neo4j
