# The vector store's make targets (extract-portfolio), joined to the core's by GNU make's multiple rules per target: the
# host-side indexer's infrastructure includes Qdrant while this folder exists.

.PHONY: infra-qdrant

infra-qdrant: require-docker
	@$(COMPOSE) up -d --wait qdrant

infra: infra-qdrant
