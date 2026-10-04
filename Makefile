# maf-lab — every workflow is a make target. Plain `make` starts the whole lab on http://localhost:7171.
# Compatible with GNU Make 3.81 (the macOS default): multi-line logic lives in scripts/*.sh.
# `make help` lists the targets. Variables can be overridden: `make up API_REPLICAS=3`.

SHELL := /bin/bash
.DEFAULT_GOAL := all

ROOT          := $(CURDIR)
COMPOSE_FILE  := $(ROOT)/compose/docker-compose.yml
# CI_MODE=1 swaps the model backend for a deterministic stub (no downloads, no secrets) — see compose/docker-compose.ci.yml.
CI_MODE       ?= 0
# The compose project, and so its volumes: ci-e2e runs as maf-lab-e2e and never touches the dev stack's data. Exported
# as COMPOSE_PROJECT_NAME too, so the scripts' own `docker compose` calls address the same project.
COMPOSE_PROJECT ?= maf-lab
export COMPOSE_PROJECT_NAME := $(COMPOSE_PROJECT)
ifeq ($(CI_MODE),1)
COMPOSE       := docker compose -p $(COMPOSE_PROJECT) -f $(COMPOSE_FILE) -f $(ROOT)/compose/docker-compose.ci.yml
else
COMPOSE       := docker compose -p $(COMPOSE_PROJECT) -f $(COMPOSE_FILE)
# The A2A, MCP and Redis inspectors run with the dev stack, never in CI. Exported, so the scripts' own compose calls
# address the same set of services.
export COMPOSE_PROFILES := inspectors
endif
# compose/.env (git-ignored, machine-local) is read by compose itself; make reads it too, so the host-side CLIs see the
# same values (e.g. OLLAMA_*_THREADS). As with compose, the environment and the command line win over the file.
# Values are taken literally: no quotes, no `export` prefix. Secrets come only from the environment, so make skips
# JEV_MAF_LAB and any *_KEY, *_TOKEN, *_SECRET or *_PASSWORD line in the file (compose still reads them for itself).
COMPOSE_ENV_FILE := $(ROOT)/compose/.env
COMPOSE_ENV_SECRET := ^(JEV_MAF_LAB|[A-Za-z0-9_]*_(KEY|TOKEN|SECRET|PASSWORD))=
ifneq ($(wildcard $(COMPOSE_ENV_FILE)),)
COMPOSE_ENV_LINES := $(shell grep -vE '$(COMPOSE_ENV_SECRET)' $(COMPOSE_ENV_FILE) | sed -nE 's/ /__SP__/g; s/^([A-Za-z_][A-Za-z0-9_]*)=/\1?=/p')
$(foreach line,$(COMPOSE_ENV_LINES),$(eval $(subst __SP__, ,$(line))))
export $(shell grep -vE '$(COMPOSE_ENV_SECRET)' $(COMPOSE_ENV_FILE) | sed -nE 's/^([A-Za-z_][A-Za-z0-9_]*)=.*/\1/p')
endif

# ── configuration (override on the command line or in the environment) ─────────────────────────────────────────────
BASE_URL      ?= http://localhost:7171
API_REPLICAS  ?= 2
MCP_REPLICAS  ?= 2
PORTFOLIO_REPLICAS ?= 2
COMPLIANCE_REPLICAS ?= 2
CHAT_MODEL    ?= gpt-oss:120b
SUITE         ?= all
WAIT_TIMEOUT  ?= 300
TO            ?= dense_v3
FORCE         ?= 0
OPENSPEC_VERSION ?= 1.13.1
# Reuse models a host Ollama already pulled, when there is one; set OLLAMA_MODELS_DIR= to use the compose volume.
OLLAMA_MODELS_DIR ?= $(shell test -d $(HOME)/.ollama && echo $(HOME)/.ollama)
export CHAT_MODEL OLLAMA_MODELS_DIR
# The repository, mounted into the api (read-write), the test agent and the coverage runner (read-only) at this same path.
MAF_LAB_REPO  ?= $(ROOT)
export MAF_LAB_REPO
# The user the api runs as, so what it writes there (branches, merges, evals/, data/) is yours, not root's. Docker
# Desktop maps bind mounts to you anyway; on rootless Docker or userns-remap set both to 0 (DECISIONS.md §70).
MAF_LAB_UID   ?= $(shell id -u)
MAF_LAB_GID   ?= $(shell id -g)
export MAF_LAB_UID MAF_LAB_GID
# The throwaway repository ci-e2e mounts instead, because its test-generation run merges into main.
E2E_REPO      ?= $(ROOT)/.cache/e2e-repo
# OLLAMA_API_KEY, JEV_MAF_LAB and GITHUB_ISSUES_TOKEN are only ever read from the environment (never written to a
# file or echoed).

# ── tools ────────────────────────────────────────────────────────────────────────────────────────────────────────
# Prefer ~/.dotnet (where `make setup` installs the SDK global.json pins) over a system dotnet that may lack it.
DOTNET ?= $(shell test -x $(HOME)/.dotnet/dotnet && echo $(HOME)/.dotnet/dotnet || command -v dotnet 2>/dev/null || echo $(HOME)/.dotnet/dotnet)
NPM    ?= $(shell command -v npm 2>/dev/null || echo npm)
ifeq ($(DOTNET),$(HOME)/.dotnet/dotnet)
export DOTNET_ROOT := $(HOME)/.dotnet
endif
export DOTNET
export DOTNET_CLI_TELEMETRY_OPTOUT := 1
export DOTNET_NOLOGO := 1
# Host-side CLIs use the compose infrastructure: Qdrant on 6333/6334, query embeddings from the interactive Ollama on
# 11435 and document embeddings from the batch Ollama on 11436, each with the thread count its CPUs were sized for.
# The graph store's Bolt port is published on loopback (7687) for these CLIs, with the compose password.
HOST_ENV := Models__OllamaEndpoint=http://localhost:11435 Models__OllamaNumThread=$${OLLAMA_INTERACTIVE_THREADS:-4} \
	Models__BatchOllamaEndpoint=http://localhost:11436 Models__BatchOllamaNumThread=$${OLLAMA_BATCH_THREADS:-12} \
	Neo4j__Uri=bolt://localhost:7687 Neo4j__Password=$${NEO4J_PASSWORD:-maf-lab-dev-graph}
# The portfolio domain is indexed by the same indexer into its own collection and BM25 vocabulary.
PORTFOLIO_ENV := Indexing__CorpusRoot=$(ROOT)/data-portfolio Qdrant__Collection=maf_portfolio_chunks Qdrant__MetaCollection=maf_portfolio_meta
# The codebase is indexed from the repository itself, by structure, into its own collection: chunks sized in embedding
# tokens (well under embeddinggemma's 2048), BM25 over identifiers split into their words (add-codebase-search).
CODE_ENV := Indexing__Layout=repository Indexing__CorpusRoot=$(ROOT) Indexing__MaxChunkTokens=1024 Indexing__Bm25Tokenizer=code \
            Qdrant__Collection=maf_code_chunks Qdrant__MetaCollection=maf_code_meta
# The indexer runs from its build output: `dotnet run` re-evaluates and checks the build on every call (3-20 s each,
# three calls per `make index`), which made a repeat run over an unchanged corpus slow. The dll is rebuilt only when a
# source, project or build file of it or of a project it references is newer than it, and touched so an up-to-date
# build is not re-checked next time.
INDEXER_DLL  := src/Maf.Lab.Indexing/bin/Debug/net10.0/Maf.Lab.Indexing.dll
INDEXER_SRC  := $(shell find src/Maf.Lab.Indexing src/Maf.Lab.Retrieval src/Maf.Lab.Domain src/Maf.Lab.Hosting -type f \( -name '*.cs' -o -name '*.csproj' -o -name '*.json' \) -not -path '*/bin/*' -not -path '*/obj/*' 2>/dev/null) \
                Directory.Build.props Directory.Packages.props global.json
INDEXER      := $(DOTNET) $(INDEXER_DLL)

.PHONY: all help up down restart ps logs clean infra index index-portfolio index-code graph reindex ask screenshots drift migrate test test-dotnet test-web lint verify \
        coverage testgen-e2e eval eval-accept eval-selection eval-retrieval eval-generation eval-injection eval-presentation eval-answer-check eval-code-route eval-graph-depth eval-retrieval-backends eval-a2a neo4j-chunks dev doctor banner index-if-empty \
        specs docs docs-check lint-dotnet lint-web build-web ci ci-e2e setup \
        require-docker require-dotnet require-npm require-python

all: require-docker up index-if-empty banner ## Start everything: build, run, wait for health, index if empty (default)

help: ## List the targets
	@echo "maf-lab — make targets (variables: API_REPLICAS MCP_REPLICAS PORTFOLIO_REPLICAS COMPLIANCE_REPLICAS CHAT_MODEL SUITE BASE_URL WAIT_TIMEOUT TO FORCE)"
	@awk 'BEGIN {FS = ":.*## "} /^[a-zA-Z0-9_-]+:.*## / {printf "  \033[36m%-16s\033[0m %s\n", $$1, $$2}' $(MAKEFILE_LIST)

# ── lifecycle ────────────────────────────────────────────────────────────────────────────────────────────────────
up: require-docker ## Build and start the stack (replicas via API_REPLICAS/MCP_REPLICAS/PORTFOLIO_REPLICAS/COMPLIANCE_REPLICAS), wait until healthy
	@if [ "$(CI_MODE)" != "1" ] && [ -z "$$OLLAMA_API_KEY" ]; then echo "⚠ OLLAMA_API_KEY is not set: the stack starts, but chat (Ollama Cloud) will fail. Run 'make setup'."; fi
	@if [ "$(CI_MODE)" != "1" ] && [ -z "$$JEV_MAF_LAB" ]; then echo "⚠ JEV_MAF_LAB is not set: the stack starts, but no turn is classified (nothing forced to search)."; fi
	@# Earlier versions ran the api as root; give back to you whatever it left owned by root in the checkout.
	@scripts/repair_ownership.sh "$(ROOT)" "$(MAF_LAB_REPO)"
	@# compose itself waits for the balancer's dependencies to be healthy; if that fails, show which service and why.
	$(COMPOSE) up -d --build --remove-orphans --scale api=$(API_REPLICAS) --scale mcp-retrieval=$(MCP_REPLICAS) \
	  --scale mcp-portfolio=$(PORTFOLIO_REPLICAS) --scale compliance=$(COMPLIANCE_REPLICAS) \
	  || { scripts/wait_healthy.sh 0; exit 1; }
	@scripts/wait_healthy.sh $(WAIT_TIMEOUT)
	@# The balancer resolves the replicas when it (re)loads; reload so it sees the current set after scaling/recreation.
	@$(COMPOSE) exec -T lb nginx -c /etc/nginx/lb/nginx.conf -s reload >/dev/null 2>&1 && echo "✓ load balancer reloaded ($(API_REPLICAS) api, $(MCP_REPLICAS) mcp, $(PORTFOLIO_REPLICAS) portfolio, $(COMPLIANCE_REPLICAS) compliance replicas)"

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
	@if [ "$(CI_MODE)" != "1" ]; then echo "  inspectors    →  A2A http://localhost:7172 · MCP http://localhost:7173 · Redis http://localhost:7174 · Neo4j http://localhost:7175"; fi
	@echo ""

# ── data ─────────────────────────────────────────────────────────────────────────────────────────────────────────
infra: require-docker ## Start only the indexer's infrastructure (Qdrant, Neo4j, both Ollama instances + the embedding model) and wait until healthy
	@# Host-side indexer CLIs need Qdrant (6333/6334), Neo4j (7687) and both compose Ollamas (11435 queries, 11436
	@# documents); a no-op when the stack is already up.
	@$(COMPOSE) up -d --wait qdrant neo4j ollama ollama-batch
	@$(COMPOSE) up --no-log-prefix ollama-init ollama-warm

index-if-empty: require-dotnet
	@$(HOST_ENV) scripts/index_if_empty.sh

$(INDEXER_DLL): $(INDEXER_SRC) | require-dotnet
	@echo "… building the indexer"
	@$(DOTNET) build src/Maf.Lab.Indexing -v quiet -nologo
	@touch $@

index: require-dotnet infra $(INDEXER_DLL) ## Index both domains' corpora and the codebase, then build the graph (unchanged documents are skipped)
	$(HOST_ENV) $(INDEXER) index
	$(HOST_ENV) $(PORTFOLIO_ENV) $(INDEXER) index
	$(HOST_ENV) $(CODE_ENV) $(INDEXER) index
	$(HOST_ENV) $(INDEXER) graph

graph: require-dotnet infra $(INDEXER_DLL) ## Build the Neo4j graph: billing relationships and the code graph (unchanged nodes are not rewritten)
	$(HOST_ENV) $(INDEXER) graph

neo4j-chunks: require-dotnet infra $(INDEXER_DLL) ## Spike: copy the billing and portfolio chunks from Qdrant into Neo4j for eval-retrieval-backends
	$(HOST_ENV) $(INDEXER) neo4j-chunks
	$(HOST_ENV) $(PORTFOLIO_ENV) $(INDEXER) neo4j-chunks
	@printf 'store size: neo4j %s · qdrant %s\n' "$$($(COMPOSE) exec -T neo4j du -sh /data/databases/neo4j 2>/dev/null | cut -f1)" "$$($(COMPOSE) exec -T qdrant du -shc /qdrant/storage/collections/maf_chunks /qdrant/storage/collections/maf_portfolio_chunks 2>/dev/null | tail -1 | cut -f1)"

index-portfolio: require-dotnet infra $(INDEXER_DLL) ## Index the portfolio corpus (data-portfolio/ → maf_portfolio_chunks) only
	$(HOST_ENV) $(PORTFOLIO_ENV) $(INDEXER) index

index-code: require-dotnet infra $(INDEXER_DLL) ## Index the repository itself (→ maf_code_chunks, served by mcp-code) only; unchanged files are skipped
	$(HOST_ENV) $(CODE_ENV) $(INDEXER) index

reindex: require-dotnet infra $(INDEXER_DLL) ## Re-embed every document of both domains (--force)
	$(HOST_ENV) $(INDEXER) index --force
	$(HOST_ENV) $(PORTFOLIO_ENV) $(INDEXER) index --force
	$(HOST_ENV) $(CODE_ENV) $(INDEXER) index --force

drift: require-dotnet infra $(INDEXER_DLL) ## Report stale documents: the index and the billing graph against the source
	$(HOST_ENV) $(INDEXER) drift

rebuild-index: require-dotnet infra $(INDEXER_DLL) ## Re-create the collection with every configured dense vector and re-index (asks unless FORCE=1)
	$(HOST_ENV) $(INDEXER) rebuild $(if $(filter 1,$(FORCE)),--yes,)

migrate: require-dotnet infra $(INDEXER_DLL) ## Fill a provisioned dense vector with its configured model (TO=dense_v3)
	$(HOST_ENV) $(INDEXER) migrate --to $(TO)

# ── quality ──────────────────────────────────────────────────────────────────────────────────────────────────────
test: test-dotnet test-web ## Run all tests (.NET unit + integration, web)

test-dotnet: require-dotnet require-docker ## .NET tests (integration tests start Qdrant and Neo4j via Testcontainers)
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

docs: require-python ## Rewrite the generated blocks in README, project.md, config.yaml and the Copilot instructions
	python3 scripts/docs.py generate

docs-check: require-python ## Check the docs against the code (generated blocks, routes, make targets, models, links); changes nothing
	@python3 -m unittest discover -s scripts/tests -q
	python3 scripts/docs.py check

ci: specs docs-check lint-dotnet test-dotnet lint-web test-web build-web ci-e2e ## Run locally what GitHub Actions runs on every pull request

ci-e2e: require-docker require-dotnet ## Model-free end-to-end: stack with the Ollama stub, index, verify, A2A conformance, test generation (CI mode)
	@# Test generation merges into main: it runs on a fresh clone of the committed HEAD, never on this checkout's main.
	@# The clone's main is this checkout's HEAD: the commit under test, not whatever local main happens to be.
	rm -rf $(E2E_REPO) && git clone -q $(ROOT) $(E2E_REPO) && git -C $(E2E_REPO) checkout -q -B main $$(git rev-parse HEAD)
	@# Its own compose project (maf-lab-e2e): the dev stack is stopped, its data kept; a pass removes the e2e stack.
	@scripts/ci_e2e.sh $(E2E_REPO) WAIT_TIMEOUT=$(WAIT_TIMEOUT)

testgen-e2e: ## Model-free test generation end to end: refresh, run, verify, accept (used by ci-e2e, against its clone)
	scripts/testgen_e2e.sh $(BASE_URL)

coverage: require-docker ## Refresh the coverage snapshot at main (both toolchains, through the running stack)
	@scripts/coverage_refresh.sh $(BASE_URL)

verify: ## Verify the running stack through the load balancer (37 checks), then AG-UI conformance of every agent (8 checks)
	scripts/verify_lb.sh $(BASE_URL)
	@[ -d copilot-runtime/node_modules ] || (cd copilot-runtime && $(NPM) ci --no-audit --no-fund >/dev/null)
	MODEL_FREE=$(CI_MODE) node copilot-runtime/conformance.mjs $(BASE_URL)

eval: require-dotnet ## Run evals (SUITE=all|selection|retrieval|generation|injection|confirmation|intent|domain|presentation|guardrail|answer-check|code-route|graph-depth|generation-judge) against the stack's MCP servers
	Evals__McpEndpoint=$(BASE_URL)/mcp Evals__PortfolioMcpEndpoint=$(BASE_URL)/portfolio/mcp Evals__CodeMcpEndpoint=$(BASE_URL)/code/mcp $(HOST_ENV) $(DOTNET) run --project src/Maf.Lab.Eval -- --suite $(SUITE)$(if $(REPEAT), --repeat $(REPEAT))

EVAL_HOST = Evals__McpEndpoint=$(BASE_URL)/mcp Evals__PortfolioMcpEndpoint=$(BASE_URL)/portfolio/mcp Evals__CodeMcpEndpoint=$(BASE_URL)/code/mcp $(HOST_ENV) $(DOTNET) run --project src/Maf.Lab.Eval --
EVAL = $(EVAL_HOST) --suite

ask: require-dotnet ## Ask one question through the agent and print its trace (Q="…" FIRM=firm-a), e.g. a cross-domain one
	$(EVAL_HOST) --ask "$(Q)" --firm $(or $(FIRM),firm-a)

screenshots: require-npm ## Re-take the README screenshots from the running stack into docs/screenshots (SHOTS=chat,topology for a subset)
	@curl -fsS -o /dev/null $(BASE_URL)/dev/users || { echo "✗ The stack is not answering on $(BASE_URL); run 'make' first."; exit 1; }
	@test -d tools/screenshots/node_modules || (cd tools/screenshots && $(NPM) ci --no-audit --no-fund)
	@cd tools/screenshots && npx playwright install chromium >/dev/null
	cd tools/screenshots && BASE_URL=$(BASE_URL) SHOTS=$(SHOTS) node capture.mjs

eval-accept: require-dotnet ## Run the evals and accept their metrics as the new baseline (REPEAT=N: mean of N runs; commit the result)
	$(EVAL) $(SUITE) --accept-baseline$(if $(REPEAT), --repeat $(REPEAT))

eval-selection: require-dotnet ## Eval: tool selection (recall/precision)
	$(EVAL) selection

eval-retrieval: require-dotnet ## Eval: retrieval (recall@5/@20, MRR per mode)
	$(EVAL) retrieval

eval-generation: require-dotnet ## Eval: answers graded by Jev, mean of 3 runs (REPEAT=N to change)
	$(EVAL) generation$(if $(REPEAT), --repeat $(REPEAT))

eval-injection: require-dotnet ## Eval: prompt-injection pass rate
	$(EVAL) injection

eval-confirmation: require-dotnet ## Eval: does the summary a person approves say what would happen
	$(EVAL) confirmation

eval-intent: require-dotnet ## Eval: intent classifier alone — would each question force search_documents? (needs JEV_MAF_LAB)
	$(EVAL) intent

eval-guardrail: require-dotnet ## Eval: content guard alone — are malicious prompts/tool results flagged and benign ones not? (needs JEV_MAF_LAB)
	$(EVAL) guardrail

eval-presentation: require-dotnet ## Eval: do portfolio answers build on their data cards instead of restating them?
	$(EVAL) presentation

eval-answer-check: require-dotnet ## Eval: Jev's answer check alone — are labelled unsupported answers flagged and supported ones not? (needs JEV_MAF_LAB)
	$(EVAL) answer-check

eval-code-route: require-dotnet ## Eval: Jev's code-route answer alone — would each code question start with the right graph call or the search? (needs JEV_MAF_LAB)
	$(EVAL) code-route

eval-retrieval-backends: require-dotnet ## Spike comparison: retrieval cases on Qdrant and on Neo4j side by side, never gated (run make neo4j-chunks first)
	$(EVAL) retrieval-backends

eval-graph-depth: require-dotnet ## Comparison: code graph traces at depth 2, 3 and 4, side by side, never gated (STRUCTURAL=1 for no model)
	$(EVAL) graph-depth $(if $(STRUCTURAL),--structural-only)

eval-a2a: require-dotnet ## Conformance: an outside client drives the agents through evals/a2a-conformance.jsonl
	@# Not $(EVAL): this one is deliberately not run by the harness, which links against the service. See DECISIONS.md.
	$(DOTNET) run --project tools/Maf.Lab.A2AProbe -- $(BASE_URL)

# ── local development ────────────────────────────────────────────────────────────────────────────────────────────
dev: require-docker require-dotnet require-npm ## Run mcp/api/web locally without Docker (infra stays in compose); Ctrl-C stops
	DOTNET=$(DOTNET) NPM=$(NPM) scripts/dev.sh

# ── setup ────────────────────────────────────────────────────────────────────────────────────────────────────────
doctor: ## Check prerequisites (Docker, .NET SDK, Node/npm, make, OLLAMA_API_KEY, JEV_MAF_LAB, MAF_LAB_REPO, GITHUB_ISSUES_TOKEN)
	@DOTNET=$(DOTNET) NPM=$(NPM) scripts/doctor.sh

setup: ## Install what 'make doctor' reports missing (.NET SDK unattended; prints the rest)
	@DOTNET=$(DOTNET) NPM=$(NPM) scripts/setup.sh

require-docker:
	@command -v docker >/dev/null 2>&1 && docker compose version >/dev/null 2>&1 || { echo "✗ Docker with the compose plugin is required (see 'make doctor'; 'make setup' installs what it can)."; exit 1; }

require-dotnet:
	@command -v $(DOTNET) >/dev/null 2>&1 || test -x $(DOTNET) || { echo "✗ The .NET SDK is required (global.json pins $$(python3 -c 'import json;print(json.load(open("global.json"))["sdk"]["version"])')). Run 'make setup'."; exit 1; }

require-python:
	@python3 -c 'import sys; sys.exit(sys.version_info < (3, 11))' 2>/dev/null || { echo "✗ Python 3.11 or newer is required for the docs targets (tomllib)."; exit 1; }

require-npm:
	@command -v $(NPM) >/dev/null 2>&1 || { echo "✗ Node.js/npm is required (see 'make doctor'; 'make setup' installs what it can)."; exit 1; }
