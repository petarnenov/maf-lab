# maf-lab — every workflow is a make target. Plain `make` starts the whole lab on http://localhost:7171.
# Compatible with GNU Make 3.81 (the macOS default): multi-line logic lives in scripts/*.sh.
# `make help` lists the targets. Variables can be overridden: `make up API_REPLICAS=3`.

SHELL := /bin/bash
.DEFAULT_GOAL := all

ROOT          := $(CURDIR)
# CI_MODE=1 swaps the model backend for a deterministic stub (no downloads, no secrets) — see compose/docker-compose.ci.yml.
CI_MODE       ?= 0
# The compose project, and so its volumes: ci-e2e runs as maf-lab-e2e and never touches the dev stack's data. Exported
# as COMPOSE_PROJECT_NAME too, so the scripts' own `docker compose` calls address the same project.
COMPOSE_PROJECT ?= maf-lab
export COMPOSE_PROJECT_NAME := $(COMPOSE_PROJECT)
# compose/.env (git-ignored, machine-local) is read by compose itself; make reads it too, so the host-side CLIs see the
# same values (e.g. OLLAMA_*_THREADS, MAF_PLUGINS). As with compose, the environment and the command line win over the
# file. Values are taken literally: no quotes, no `export` prefix. Secrets come only from the environment, so make skips
# JEV_MAF_LAB and any *_KEY, *_TOKEN, *_SECRET or *_PASSWORD line in the file (compose still reads them for itself).
COMPOSE_ENV_FILE := $(ROOT)/compose/.env
COMPOSE_ENV_SECRET := ^(JEV_MAF_LAB|[A-Za-z0-9_]*_(KEY|TOKEN|SECRET|PASSWORD))=
ifneq ($(wildcard $(COMPOSE_ENV_FILE)),)
COMPOSE_ENV_LINES := $(shell grep -vE '$(COMPOSE_ENV_SECRET)' $(COMPOSE_ENV_FILE) | sed -nE 's/ /__SP__/g; s/^([A-Za-z_][A-Za-z0-9_]*)=/\1?=/p')
$(foreach line,$(COMPOSE_ENV_LINES),$(eval $(subst __SP__, ,$(line))))
export $(shell grep -vE '$(COMPOSE_ENV_SECRET)' $(COMPOSE_ENV_FILE) | sed -nE 's/^([A-Za-z_][A-Za-z0-9_]*)=.*/\1/p')
endif

# ── plugins (introduce-plugins) ──────────────────────────────────────────────────────────────────────────────────
# MAF_PLUGINS: unset or empty installs every bundled plugin MAF_ENV allows (except _example), `none` installs none,
# otherwise a comma-separated list; dependencies are added. MAF_ENV: dev | qa | stage | prod. scripts/plugins.py
# resolves the set; `make up` writes it to plugins/.installed and regenerates compose/lb/conf.d from it.
MAF_ENV       ?= dev
MAF_PLUGINS   ?=
ifeq ($(CI_MODE),1)
# CI's plugin set, a positive list kept here only (introduce-plugins 5.1): CI_MODE means no downloads and no secrets,
# so the developer tools stay out (a2a-inspector even builds from a git context). Each plugin extracted from the core
# adds itself here in the same commit.
CI_PLUGINS    ?= billing,code,monitor,conversation-history,_example
MAF_PLUGINS   := $(CI_PLUGINS)
endif
export MAF_ENV MAF_PLUGINS
# The set passed explicitly: make 3.81's $(shell) does not see the variables make exports.
PLUGINS_PY     = MAF_ENV='$(MAF_ENV)' MAF_PLUGINS='$(MAF_PLUGINS)' CI_MODE='$(CI_MODE)' python3 $(ROOT)/scripts/plugins.py
# The image variant (introduce-plugins decision 5e): full for dev, product (no dev-or-qa-only plugin code) for qa, stage
# and prod, so stage and prod promote exactly the image qa tested. qa may run full beside it with MAF_IMAGE_VARIANT=full.
MAF_IMAGE_VARIANT ?= $(if $(filter qa stage prod,$(MAF_ENV)),product,full)
MAF_PRODUCT_PLUGINS := $(shell $(PLUGINS_PY) product-servers 2>/dev/null)
MAF_PRODUCT_WEB := $(shell $(PLUGINS_PY) product-plugins 2>/dev/null)
export MAF_IMAGE_VARIANT MAF_PRODUCT_PLUGINS MAF_PRODUCT_WEB
# Each installed plugin's compose file holds only its own services; it is merged after the core's.
PLUGIN_COMPOSE_FILES := $(shell $(PLUGINS_PY) compose-files 2>/dev/null)
COMPOSE_BASE  := $(ROOT)/compose/docker-compose.yml
ifeq ($(CI_MODE),1)
COMPOSE_FILES := $(COMPOSE_BASE) $(ROOT)/compose/docker-compose.ci.yml $(PLUGIN_COMPOSE_FILES)
else
# The dev-only override: the inspector plugins' ports, published by lb (whose network they share). Never loaded in CI,
# and never in stage or prod, which run without it.
COMPOSE_FILES := $(COMPOSE_BASE) $(ROOT)/compose/docker-compose.dev.yml $(PLUGIN_COMPOSE_FILES)
endif
empty :=
space := $(empty) $(empty)
# Exported, so every script's own `docker compose` call (wait_healthy.sh, plugin_switch.sh) loads the same files.
export COMPOSE_FILE := $(subst $(space),:,$(strip $(COMPOSE_FILES)))
COMPOSE       := docker compose -p $(COMPOSE_PROJECT)
# Each plugin's own make targets.
-include $(wildcard $(ROOT)/plugins/*/plugin.mk)

# ── configuration (override on the command line or in the environment) ─────────────────────────────────────────────
BASE_URL      ?= http://localhost:7171
API_REPLICAS  ?= 2
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
# The user the api runs as, so what it writes there (branches, merges, evals/) is yours, not root's. Docker
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
# The indexer runs from its build output: `dotnet run` re-evaluates and checks the build on every call (3-20 s each,
# three calls per `make index`), which made a repeat run over an unchanged corpus slow. The dll is rebuilt only when a
# source, project or build file of it or of a project it references is newer than it, and touched so an up-to-date
# build is not re-checked next time.
INDEXER_DLL  := src/Maf.Lab.Indexing/bin/Debug/net10.0/Maf.Lab.Indexing.dll
INDEXER_SRC  := $(shell find src/Maf.Lab.Indexing src/Maf.Lab.Retrieval src/Maf.Lab.Domain src/Maf.Lab.Hosting -type f \( -name '*.cs' -o -name '*.csproj' -o -name '*.json' \) -not -path '*/bin/*' -not -path '*/obj/*' 2>/dev/null) \
                Directory.Build.props Directory.Packages.props global.json
INDEXER      := $(DOTNET) $(INDEXER_DLL)

.PHONY: all help up core plugins plugin-new plugin-new-check plugin-switch-check plugin-on plugin-off product-check down restart ps logs print-compose-file clean infra index index-portfolio indexer graph reindex ask screenshots drift migrate test test-dotnet test-web lint verify \
        coverage testgen-e2e eval eval-accept eval-selection eval-retrieval eval-generation eval-injection eval-presentation eval-answer-check eval-code-route eval-graph-depth eval-a2a dev doctor banner index-if-empty \
        specs docs docs-check lint-dotnet lint-web build-web ci ci-e2e ci-e2e-core core-turn-check setup \
        require-docker require-dotnet require-npm require-python

all: require-docker up index-if-empty banner ## Start everything: build, run, wait for health, index if empty (default)

help: ## List the targets
	@echo "maf-lab — make targets (variables: API_REPLICAS PORTFOLIO_REPLICAS COMPLIANCE_REPLICAS CHAT_MODEL SUITE BASE_URL WAIT_TIMEOUT TO FORCE)"
	@awk 'BEGIN {FS = ":.*## "} /^[a-zA-Z0-9_-]+:.*## / {printf "  \033[36m%-16s\033[0m %s\n", $$1, $$2}' $(MAKEFILE_LIST)

# ── lifecycle ────────────────────────────────────────────────────────────────────────────────────────────────────
up: require-docker ## Build and start the stack (replicas via API_REPLICAS/PORTFOLIO_REPLICAS/COMPLIANCE_REPLICAS), wait until healthy
	@if [ "$(CI_MODE)" != "1" ] && [ -z "$$OLLAMA_API_KEY" ]; then echo "⚠ OLLAMA_API_KEY is not set: the stack starts, but chat (Ollama Cloud) will fail. Run 'make setup'."; fi
	@if [ "$(CI_MODE)" != "1" ] && [ -z "$$JEV_MAF_LAB" ]; then echo "⚠ JEV_MAF_LAB is not set: the stack starts, but no turn is classified (nothing forced to search)."; fi
	@# Earlier versions ran the api as root; give back to you whatever it left owned by root in the checkout.
	@scripts/repair_ownership.sh "$(ROOT)" "$(MAF_LAB_REPO)"
	@# The installed plugin set (the api reads it at start), and the balancer's conf.d in two stages, as plugin-on does
	@# (spec load-balancing): first the core's alone (the api upstream from its template, no plugin snippet, so the lb
	@# never names a service that is not up yet, and a stale snippet from another set is gone), then, once every service
	@# is healthy, each plugin's snippets and a checked, graceful reload.
	@$(PLUGINS_PY) install --installed-only
	@MAF_PLUGINS=none $(PLUGINS_PY) install --conf-d-only
	@# compose itself waits for the balancer's dependencies to be healthy; if that fails, show which service and why.
	$(COMPOSE) up -d --build --remove-orphans --scale api=$(API_REPLICAS) \
	  --scale mcp-portfolio=$(PORTFOLIO_REPLICAS) --scale compliance=$(COMPLIANCE_REPLICAS) \
	  || { scripts/wait_healthy.sh 0; exit 1; }
	@scripts/wait_healthy.sh $(WAIT_TIMEOUT)
	@# The plugins' snippets, now that their services are healthy; the balancer resolves the replicas when it reloads, so
	@# this reload also picks up the current set after scaling or recreation. A configuration nginx refuses fails make up.
	@$(PLUGINS_PY) install --conf-d-only
	@$(COMPOSE) exec -T lb nginx -t -q -c /etc/nginx/lb/nginx.conf \
	  && $(COMPOSE) exec -T lb nginx -c /etc/nginx/lb/nginx.conf -s reload >/dev/null 2>&1 \
	  && echo "✓ load balancer reloaded ($(API_REPLICAS) api, $(PORTFOLIO_REPLICAS) portfolio, $(COMPLIANCE_REPLICAS) compliance replicas)"
	@# Every replica re-reads plugins/.installed now rather than at its next 30-second check.
	@$(COMPOSE) exec -T redis redis-cli PUBLISH plugins-changed up >/dev/null 2>&1 || true

core: ## Start the core with no plugin and no built-in domain (MAF_PLUGINS=none); declines every turn (decision 5h); a plain make brings them back
	@Agent__BuiltInDomains= $(MAKE) --no-print-directory up MAF_PLUGINS=none

product-check: require-docker ## Build the product image variant (api, web) and check it holds no dev-or-qa-only plugin code
	@MAF_IMAGE_VARIANT=product $(COMPOSE) build api web
	@scripts/check_product_image.sh

plugins: ## List every plugin: kind, scope, environments, whether installed, dependencies, description
	@$(PLUGINS_PY) list

plugin-new: ## Start a new plugin (NAME=…, KIND=mcp|app): mcp copies _example, app renders the app template; prints the files written
	@$(PLUGINS_PY) new '$(NAME)' '$(KIND)'

plugin-switch-check: require-docker ## On the running dev stack: switch the code plugin off and on, checking a run in flight, availability and that nothing else restarts
	@scripts/plugin_switch_check.sh

plugin-new-check: require-dotnet ## Scaffold one plugin of each kind, build them, check the docs, then remove them (CI)
	@DOTNET=$(DOTNET) scripts/plugin_new_check.sh

plugin-on: require-docker ## Install one plugin into the running stack (NAME=…): its services, healthy, then its routes; ALLOW_DOWNTIME=1 for one api replica
	@scripts/plugin_switch.sh on "$(NAME)"

plugin-off: require-docker ## Remove one plugin from the running stack (NAME=…); refuses while it has open work unless STOP_WORK=1
	@scripts/plugin_switch.sh off "$(NAME)"

down: require-docker ## Stop the stack (data volumes are kept)
	$(COMPOSE) down --remove-orphans

restart: down up ## Stop and start the stack

ps: require-docker ## Show services, state and health
	@{ printf 'NAME\tSTATE\tHEALTH\tPORTS\n'; $(COMPOSE) ps -a --format '{{.Name}}\t{{.State}}\t{{if .Health}}{{.Health}}{{else}}-{{end}}\t{{.Ports}}'; } | column -t -s $$'\t'

logs: require-docker ## Follow logs (SERVICE=api to narrow)
	$(COMPOSE) logs -f --tail 100 $(SERVICE)

# The compose files make loads (core, override, installed plugins'), for a caller outside make: CI's diagnostics.
print-compose-file:
	@echo "$(COMPOSE_FILE)"

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
	@echo "  plugins       →  make plugins (each one's links are in the web UI's navigation)"
	@echo ""

# ── data ─────────────────────────────────────────────────────────────────────────────────────────────────────────
infra: require-docker ## Start only the indexer's infrastructure (Qdrant, both Ollama instances + the embedding model, and installed stores) and wait until healthy
	@# Host-side indexer CLIs need Qdrant (6333/6334) and both compose Ollamas (11435 queries, 11436 documents); a store
	@# plugin adds itself (the neo4j plugin's infra-neo4j). A no-op when the stack is already up.
	@$(COMPOSE) up -d --wait qdrant ollama ollama-batch
	@$(COMPOSE) up --no-log-prefix ollama-init ollama-warm

index-if-empty: require-dotnet
	@$(HOST_ENV) scripts/index_if_empty.sh

$(INDEXER_DLL): $(INDEXER_SRC) | require-dotnet
	@echo "… building the indexer"
	@$(DOTNET) build src/Maf.Lab.Indexing -v quiet -nologo
	@touch $@

# The indexer's build, by name: what a plugin's index targets depend on (a plugin.mk is read before $(INDEXER_DLL) is set).
indexer: $(INDEXER_DLL)

index: require-dotnet infra $(INDEXER_DLL) ## Index the built-in domains' corpora; each plugin present adds its corpus and graph (unchanged documents are skipped)
	$(HOST_ENV) $(PORTFOLIO_ENV) $(INDEXER) index

graph: ## Build the Neo4j graph: each plugin present adds its own (unchanged nodes are not rewritten)

index-portfolio: require-dotnet infra $(INDEXER_DLL) ## Index the portfolio corpus (data-portfolio/ → maf_portfolio_chunks) only
	$(HOST_ENV) $(PORTFOLIO_ENV) $(INDEXER) index

reindex: require-dotnet infra $(INDEXER_DLL) ## Re-embed every document of the built-in domains, and of each plugin present (--force)
	$(HOST_ENV) $(PORTFOLIO_ENV) $(INDEXER) index --force

drift: ## Report stale documents: each plugin present compares its index and graph against its source

rebuild-index: ## Re-create each plugin's collection with every configured dense vector and re-index it (asks unless FORCE=1)

migrate: ## Fill a provisioned dense vector with its configured model in each plugin's collection (TO=dense_v3)

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

ci: specs docs-check lint-dotnet test-dotnet lint-web test-web build-web ci-e2e ci-e2e-core ## Run locally what GitHub Actions runs on every pull request

ci-e2e: require-docker require-dotnet ## Model-free end-to-end: stack with the Ollama stub, index, verify, A2A conformance, test generation (CI mode)
	@# Test generation merges into main: it runs on a fresh clone of the committed HEAD, never on this checkout's main.
	@# The clone's main is this checkout's HEAD: the commit under test, not whatever local main happens to be.
	rm -rf $(E2E_REPO) && git clone -q $(ROOT) $(E2E_REPO) && git -C $(E2E_REPO) checkout -q -B main $$(git rev-parse HEAD)
	@# Its own compose project (maf-lab-e2e): the dev stack is stopped, its data kept; a pass removes the e2e stack.
	@scripts/ci_e2e.sh $(E2E_REPO) WAIT_TIMEOUT=$(WAIT_TIMEOUT)

ci-e2e-core: require-docker ## Model-free core-only end-to-end: make core with the Ollama stub, verify, and the decline with no model, Jev or tool call (CI mode)
	@E2E_PROJECT=maf-lab-e2e-core E2E_TARGETS="core verify core-turn-check" scripts/ci_e2e.sh $(ROOT) WAIT_TIMEOUT=$(WAIT_TIMEOUT)

core-turn-check: ## On a core-only stack: a turn declines with the fixed reply, and the stub saw no model or Jev call (used by ci-e2e-core)
	python3 scripts/core_turn_check.py $(BASE_URL)

testgen-e2e: ## Model-free test generation end to end: refresh, run, verify, accept (used by ci-e2e, against its clone)
	scripts/testgen_e2e.sh $(BASE_URL)

coverage: require-docker ## Refresh the coverage snapshot at main (both toolchains, through the running stack)
	@scripts/coverage_refresh.sh $(BASE_URL)

verify: ## Verify the running stack through the load balancer (37 checks), then AG-UI conformance of every agent (8 checks)
	scripts/verify_lb.sh $(BASE_URL)
	@[ -d copilot-runtime/node_modules ] || (cd copilot-runtime && $(NPM) ci --no-audit --no-fund >/dev/null)
	MODEL_FREE=$(CI_MODE) node copilot-runtime/conformance.mjs $(BASE_URL)

# The eval's agent wires the stack's compliance reviewer as the api does (compose: Compliance__*), so the fee adjustment
# that needs a reviewer is offered to it, as in a deployment (introduce-plugins 4.4).
EVAL_COMPLIANCE := Compliance__BaseUrl=$(BASE_URL)/compliance Compliance__ClientId=maf-lab-assistant \
	Compliance__ClientSecret=$${COMPLIANCE_CLIENT_SECRET:-assistant-dev-secret}

eval: require-dotnet ## Run evals (SUITE=all|selection|retrieval|generation|injection|confirmation|intent|domain|presentation|guardrail|answer-check|code-route|graph-depth|generation-judge) against the stack's MCP servers
	Evals__McpEndpoint=$(BASE_URL)/mcp Evals__PortfolioMcpEndpoint=$(BASE_URL)/portfolio/mcp Evals__CodeMcpEndpoint=$(BASE_URL)/code/mcp $(EVAL_COMPLIANCE) $(HOST_ENV) $(DOTNET) run --project src/Maf.Lab.Eval -- --suite $(SUITE)$(if $(REPEAT), --repeat $(REPEAT))

EVAL_HOST = Evals__McpEndpoint=$(BASE_URL)/mcp Evals__PortfolioMcpEndpoint=$(BASE_URL)/portfolio/mcp Evals__CodeMcpEndpoint=$(BASE_URL)/code/mcp $(EVAL_COMPLIANCE) $(HOST_ENV) $(DOTNET) run --project src/Maf.Lab.Eval --
EVAL = $(EVAL_HOST) --suite

ask: require-dotnet ## Ask one question through the agent and print its trace (Q="…" TENANT=firm-a), e.g. a cross-domain one
	$(EVAL_HOST) --ask "$(Q)" --tenant $(or $(TENANT),$(FIRM),firm-a)

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
