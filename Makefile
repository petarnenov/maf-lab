# maf-lab — every workflow is a make target. Plain `make` starts the whole lab on http://localhost:7171.
# Compatible with GNU Make 3.81 (the macOS default): multi-line logic lives in scripts/*.sh.
# `make help` lists the targets. Variables can be overridden: `make up API_REPLICAS=3`.

SHELL := /bin/bash
.DEFAULT_GOAL := all

ROOT          := $(CURDIR)
COMPOSE_FILE  := $(ROOT)/compose/docker-compose.yml
# CI_MODE=1 swaps the model backend for a deterministic stub (no downloads, no secrets) — see compose/docker-compose.ci.yml.
CI_MODE       ?= 0
ifeq ($(CI_MODE),1)
COMPOSE       := docker compose -f $(COMPOSE_FILE) -f $(ROOT)/compose/docker-compose.ci.yml
else
COMPOSE       := docker compose -f $(COMPOSE_FILE)
endif

# ── configuration (override on the command line or in the environment) ─────────────────────────────────────────────
BASE_URL      ?= http://localhost:7171
API_REPLICAS  ?= 2
MCP_REPLICAS  ?= 2
COMPLIANCE_REPLICAS ?= 2
CHAT_MODEL    ?= gpt-oss:120b
SUITE         ?= all
WAIT_TIMEOUT  ?= 300
TO            ?= dense_v2
FORCE         ?= 0
OPENSPEC_VERSION ?= 1.13.1
# Reuse models a host Ollama already pulled, when there is one; set OLLAMA_MODELS_DIR= to use the compose volume.
OLLAMA_MODELS_DIR ?= $(shell test -d $(HOME)/.ollama && echo $(HOME)/.ollama)
export CHAT_MODEL OLLAMA_MODELS_DIR
# OLLAMA_API_KEY is only ever read from the environment (never written to a file or echoed).

# ── tools ────────────────────────────────────────────────────────────────────────────────────────────────────────
DOTNET ?= $(shell command -v dotnet 2>/dev/null || echo $(HOME)/.dotnet/dotnet)
NPM    ?= $(shell command -v npm 2>/dev/null || echo npm)
ifeq ($(DOTNET),$(HOME)/.dotnet/dotnet)
export DOTNET_ROOT := $(HOME)/.dotnet
endif
export DOTNET
export DOTNET_CLI_TELEMETRY_OPTOUT := 1
export DOTNET_NOLOGO := 1
# Host-side CLIs use the compose infrastructure: Qdrant on 6333/6334, embeddings from the compose Ollama on 11435.
HOST_ENV := Models__OllamaEndpoint=http://localhost:11435

.PHONY: all help up down restart ps logs clean index reindex drift migrate test test-dotnet test-web lint verify \
        eval eval-accept eval-selection eval-retrieval eval-generation eval-injection dev doctor banner index-if-empty \
        specs lint-dotnet lint-web build-web ci ci-e2e \
        require-docker require-dotnet require-npm

all: require-docker up index-if-empty banner ## Start everything: build, run, wait for health, index if empty (default)

help: ## List the targets
	@echo "maf-lab — make targets (variables: API_REPLICAS MCP_REPLICAS COMPLIANCE_REPLICAS CHAT_MODEL SUITE BASE_URL WAIT_TIMEOUT TO FORCE)"
	@awk 'BEGIN {FS = ":.*## "} /^[a-zA-Z0-9_-]+:.*## / {printf "  \033[36m%-16s\033[0m %s\n", $$1, $$2}' $(MAKEFILE_LIST)

# ── lifecycle ────────────────────────────────────────────────────────────────────────────────────────────────────
up: require-docker ## Build and start the stack (replicas via API_REPLICAS/MCP_REPLICAS/COMPLIANCE_REPLICAS), wait until healthy
	@if [ "$(CI_MODE)" != "1" ] && [ -z "$$OLLAMA_API_KEY" ]; then echo "⚠ OLLAMA_API_KEY is not set: the stack starts, but chat (Ollama Cloud) will fail. See 'make doctor'."; fi
	@# compose itself waits for the balancer's dependencies to be healthy; if that fails, show which service and why.
	$(COMPOSE) up -d --build --remove-orphans --scale api=$(API_REPLICAS) --scale mcp-retrieval=$(MCP_REPLICAS) \
	  --scale compliance=$(COMPLIANCE_REPLICAS) \
	  || { scripts/wait_healthy.sh 0; exit 1; }
	@scripts/wait_healthy.sh $(WAIT_TIMEOUT)
	@# The balancer resolves the replicas when it (re)loads; reload so it sees the current set after scaling/recreation.
	@$(COMPOSE) exec -T lb nginx -s reload >/dev/null 2>&1 && echo "✓ load balancer reloaded ($(API_REPLICAS) api, $(MCP_REPLICAS) mcp, $(COMPLIANCE_REPLICAS) compliance replicas)"

down: require-docker ## Stop the stack (data volumes are kept)
	$(COMPOSE) down --remove-orphans

restart: down up ## Stop and start the stack

ps: require-docker ## Show services, state and health
	@{ printf 'NAME\tSTATE\tHEALTH\tPORTS\n'; $(COMPOSE) ps -a --format '{{.Name}}\t{{.State}}\t{{if .Health}}{{.Health}}{{else}}-{{end}}\t{{.Ports}}'; } | column -t -s $$'\t'

logs: require-docker ## Follow logs (SERVICE=api to narrow)
	$(COMPOSE) logs -f --tail 100 $(SERVICE)

clean: require-docker ## Remove the stack WITH volumes (index, conversations) and build outputs; asks unless FORCE=1
	@if [ "$(FORCE)" != "1" ]; then \
	  read -r -p "This deletes the Qdrant index, conversations and build outputs. Type 'yes' to continue: " answer; \
	  [ "$$answer" = "yes" ] || { echo "aborted"; exit 1; }; \
	fi
	$(COMPOSE) down -v --remove-orphans
	rm -rf src/*/bin src/*/obj tests/*/bin tests/*/obj web/dist evals/reports

banner:
	@echo ""
	@echo "  maf-lab is up →  $(BASE_URL)   (make help · make verify · make logs · make down)"
	@echo ""

# ── data ─────────────────────────────────────────────────────────────────────────────────────────────────────────
index-if-empty: require-dotnet
	@$(HOST_ENV) scripts/index_if_empty.sh

index: require-dotnet ## Index the corpus (unchanged documents are skipped)
	$(HOST_ENV) $(DOTNET) run --project src/Maf.Lab.Indexing -- index

reindex: require-dotnet ## Re-embed every document (--force)
	$(HOST_ENV) $(DOTNET) run --project src/Maf.Lab.Indexing -- index --force

drift: require-dotnet ## Report stale documents (source newer than index)
	$(HOST_ENV) $(DOTNET) run --project src/Maf.Lab.Indexing -- drift

migrate: require-dotnet ## Fill the second dense vector with the new embedding model (TO=dense_v2)
	$(HOST_ENV) $(DOTNET) run --project src/Maf.Lab.Indexing -- migrate --to $(TO)

# ── quality ──────────────────────────────────────────────────────────────────────────────────────────────────────
test: test-dotnet test-web ## Run all tests (.NET unit + integration, web)

test-dotnet: require-dotnet require-docker ## .NET tests (integration tests start Qdrant via Testcontainers)
	$(DOTNET) test --solution maf-lab.sln

test-web: require-npm ## Web tests (Vitest)
	cd web && { [ -d node_modules ] || $(NPM) ci --silent; } && $(NPM) test -- --run

lint: lint-dotnet lint-web ## Build .NET with warnings as errors; ESLint + Prettier for web

lint-dotnet: require-dotnet ## .NET build with warnings as errors
	@# --no-incremental so the analyzers run over every file, as they do on a fresh CI checkout.
	$(DOTNET) build maf-lab.sln -warnaserror -nologo -v q --no-incremental

lint-web: require-npm ## ESLint + Prettier
	cd web && { [ -d node_modules ] || $(NPM) ci --silent; } && $(NPM) run lint

build-web: require-npm ## Type-check and build the web app
	cd web && { [ -d node_modules ] || $(NPM) ci --silent; } && $(NPM) run build

specs: require-npm ## Validate all OpenSpec specs and changes (strict)
	npx --yes @fission-ai/openspec@$(OPENSPEC_VERSION) validate --all --strict --no-interactive

ci: specs lint-dotnet test-dotnet lint-web test-web build-web ci-e2e ## Run locally what GitHub Actions runs on every push

ci-e2e: require-docker require-dotnet ## Model-free end-to-end: stack with the Ollama stub, index, verify (CI mode)
	$(MAKE) up index-if-empty verify CI_MODE=1

verify: ## Verify the running stack through the load balancer (17 checks)
	scripts/verify_lb.sh $(BASE_URL)

eval: require-dotnet ## Run evals (SUITE=all|selection|retrieval|generation|injection) against the stack's MCP
	Evals__McpEndpoint=$(BASE_URL)/mcp $(HOST_ENV) $(DOTNET) run --project src/Maf.Lab.Eval -- --suite $(SUITE)

EVAL = Evals__McpEndpoint=$(BASE_URL)/mcp $(HOST_ENV) $(DOTNET) run --project src/Maf.Lab.Eval -- --suite

eval-accept: require-dotnet ## Run the evals and accept their metrics as the new baseline (commit the result)
	$(EVAL) $(SUITE) --accept-baseline

eval-selection: require-dotnet ## Eval: tool selection (recall/precision)
	$(EVAL) selection

eval-retrieval: require-dotnet ## Eval: retrieval (recall@5/@20, MRR per mode)
	$(EVAL) retrieval

eval-generation: require-dotnet ## Eval: answers judged for faithfulness/relevance
	$(EVAL) generation

eval-injection: require-dotnet ## Eval: prompt-injection pass rate
	$(EVAL) injection

# ── local development ────────────────────────────────────────────────────────────────────────────────────────────
dev: require-docker require-dotnet require-npm ## Run mcp/api/web locally without Docker (infra stays in compose); Ctrl-C stops
	DOTNET=$(DOTNET) NPM=$(NPM) scripts/dev.sh

# ── setup ────────────────────────────────────────────────────────────────────────────────────────────────────────
doctor: ## Check prerequisites (Docker, .NET SDK, Node/npm, make, OLLAMA_API_KEY)
	@DOTNET=$(DOTNET) NPM=$(NPM) scripts/doctor.sh

require-docker:
	@command -v docker >/dev/null 2>&1 && docker compose version >/dev/null 2>&1 || { echo "✗ Docker with the compose plugin is required (see 'make doctor')."; exit 1; }

require-dotnet:
	@command -v $(DOTNET) >/dev/null 2>&1 || test -x $(DOTNET) || { echo "✗ The .NET SDK is required (global.json pins $$(python3 -c 'import json;print(json.load(open("global.json"))["sdk"]["version"])')). See 'make doctor'."; exit 1; }

require-npm:
	@command -v $(NPM) >/dev/null 2>&1 || { echo "✗ Node.js/npm is required (see 'make doctor')."; exit 1; }
