# maf-lab

[![CI](https://github.com/petarnenov/maf-lab/actions/workflows/ci.yml/badge.svg)](https://github.com/petarnenov/maf-lab/actions/workflows/ci.yml)

A learning lab: a RAG-backed assistant for a TAMP, over three domains — **billing**, **portfolio** and the lab's own
**codebase** — each served by its own **MCP server** with its own retrieval **tool** (`search_documents`,
`search_portfolio_documents`, `search_codebase`) over its own **multi-tenant Qdrant** collection, consumed by a **Microsoft Agent Framework** agent, with a React chat UI, an eval
harness and tested prompt-injection defences. TypeSafe's Jev decides which domains a question belongs to, and the
monitor shows where a turn crosses from one into the other. Beside the vector store, a **Neo4j** graph answers how things
connect: billing relationships (`trace_billing_relationships`) and the code graph (`trace_code_symbol`, `change_impact`).

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/screenshots/chat-dark.png">
  <img src="docs/screenshots/chat-light.png" alt="The chat: a billing question answered with its tool calls and sources, and the behind-the-scenes monitor tracing the turn step by step">
</picture>

- Stack, layout and hard conventions: [`openspec/project.md`](openspec/project.md)
- Why things are the way they are, and every pinned version: [`DECISIONS.md`](DECISIONS.md)
- Current behaviour (specs): [`openspec/specs`](openspec/specs); every change that built it, in
  [`openspec/changes/archive`](openspec/changes/archive) — from
  [`add-day3-retrieval`](openspec/changes/archive/2026-09-19-add-day3-retrieval) and `add-load-balancer` to
  `add-portfolio-domain`, `add-compliance-agent`, `use-jev-intent-classifier` and `add-codebase-domain`
- HTTP contract between API and web: [`docs/http-api.md`](docs/http-api.md); the trace event format:
  [`docs/trace-events.md`](docs/trace-events.md); telemetry: [`docs/telemetry.md`](docs/telemetry.md); shared state:
  [`docs/shared-state.md`](docs/shared-state.md)
- Screens: `/chat`, `/evals`, `/topology`, `/telemetry`, `/coverage`, `/curriculum`, and for admins `/admin/index`, `/admin/feedback`,
  `/admin/compliance`, `/admin/jev`, `/admin/a2a`

```mermaid
flowchart TB
  clients(["👤 Browser · 🤖 A2A partner · MCP client"])
  lb{{"🚪 lb · nginx · http://localhost:7171 — the only entry point"}}

  subgraph app["Application"]
    direction LR
    web["🖥️ web<br/>React SPA · CopilotKit"]
    copilot["🔌 copilot-runtime<br/>CopilotKit runtime · wiring only"]
    api["🧠 api ×2<br/>Agent Framework<br/>AG-UI agents · A2A · admin"]
    compliance["🛡️ compliance ×2<br/>A2A reviewer"]
    testagent["🧪 test-agent<br/>A2A test generation"]
    runner["coverage-runner<br/>build + test, no egress"]
  end

  subgraph mcp["MCP servers · one per domain, reached through lb"]
    direction LR
    billing["💳 mcp-retrieval ×2<br/>billing<br/>search_documents · trace_billing_relationships"]
    portfolio["📈 mcp-portfolio ×2<br/>portfolio<br/>search_portfolio_documents"]
    code["🧩 mcp-code<br/>codebase<br/>search_codebase · ask_codebase<br/>trace_code_symbol · change_impact"]
  end

  subgraph backing["State and models"]
    direction LR
    qdrant[("Qdrant<br/>one collection per domain")]
    neo4j[("Neo4j<br/>billing + code graph")]
    sqlite[("SQLite<br/>conversations · turns · audit")]
    redis[("Redis<br/>shared state")]
    ollama["Ollama · queries<br/>embeddinggemma"]
    ollamabatch["Ollama · batch<br/>embeddinggemma"]
    cloud["☁️ Ollama Cloud<br/>gpt-oss:120b"]
    jev["TypeSafe Jev<br/>domains · guard · relevance · answer check"]
  end

  obs["📊 OTel collector → Prometheus · Jaeger"]

  clients --> lb
  lb -- "/" --> web
  lb -- "/copilotkit" --> copilot
  copilot -- "AG-UI · the caller's token" --> lb
  lb -- "/api · /dev · /a2a" --> api
  lb -- "/compliance" --> compliance
  lb -- "/mcp · /portfolio/mcp · /code/mcp" --> mcp
  api -- "A2A" --> compliance & testagent
  testagent -- "run tests" --> runner
  api -- "coverage · verify" --> runner
  api -- "MCP tools" --> mcp
  mcp -- "hybrid search · embed" --> qdrant & ollama
  mcp -- "graph lookups" --> neo4j
  mcp -- "relevance" --> jev
  api --> sqlite & redis & cloud & jev
  api -- "index runs · embed" --> ollamabatch
  app & mcp -. "OTLP" .-> obs

  classDef entry fill:#fff4e5,stroke:#f59e0b,color:#7c2d12
  classDef svc fill:#e8f1ff,stroke:#3b82f6,color:#0b2e6b
  classDef store fill:#eafaf1,stroke:#22c55e,color:#14532d
  classDef model fill:#f3e8ff,stroke:#a855f7,color:#3b0764
  classDef ext fill:#f1f5f9,stroke:#64748b,color:#0f172a
  class lb entry
  class web,copilot,api,compliance,testagent,runner,billing,portfolio,code svc
  class qdrant,neo4j,sqlite,redis store
  class ollama,ollamabatch,cloud,jev model
  class clients,obs ext
  classDef group fill:transparent,stroke:#94a3b8,stroke-dasharray:4 3
  class app,mcp,backing group
```

Everything user- and agent-facing goes through **one entry point on port 7171**. api, mcp-retrieval, mcp-portfolio and
compliance run two replicas each, mcp-code and copilot-runtime one (`X-Instance` response header shows which one answered).
test-agent and coverage-runner run one each and have no route of their own: the api reaches them inside the compose
network. The balancer also serves Jaeger at `/jaeger` and takes the browser's OTLP traces at `/v1/traces`. Besides
7171, only Qdrant and the two Ollamas are published, Neo4j's Bolt port on `127.0.0.1:7687` for the host-side indexer,
plus the [developer tools](#developer-tools) on `127.0.0.1` (7172–7175) while their plugins are installed.

The embedding model runs in **two Ollama instances**: `ollama` (11435) embeds search queries only, `ollama-batch`
(11436) embeds documents — `make index*`, `rebuild-index`, `migrate` and index runs from `/admin/index`. A batch takes
~20 s on CPU and Ollama serves one request at a time, so on one instance a search would queue behind it. Each instance
is pinned to its own CPUs (`OLLAMA_INTERACTIVE_CPUS=0-3`, `OLLAMA_BATCH_CPUS=4-15`) and every request names the
matching thread count (`OLLAMA_INTERACTIVE_THREADS=4`, `OLLAMA_BATCH_THREADS=12`): Ollama does not derive it from the
container, and a request with another count — or none, e.g. a manual `curl` — reloads the model on all CPUs. Change a
set and its thread count together; `make doctor` checks them against Docker's CPUs. To set them once per machine, put
them in the git-ignored `compose/.env` (plain `KEY=value`, no quotes): compose and make both read it, and the
environment or the make command line still wins. Secrets stay in the environment — make skips `JEV_MAF_LAB` and any
`*_KEY`, `*_TOKEN`, `*_SECRET` or `*_PASSWORD` in that file.

The balancer's routes, as `compose/lb/nginx.conf`, the api upstream template and each plugin's snippets declare them:

<!-- generated:lb-routes — edit compose/lb/nginx.conf or a plugin's lb.*.conf, then run make docs -->
| Path | Match | Served by |
|---|---|---|
| `^/[a-z0-9-]+/mcp$` | regex | the balancer itself |
| `^/[a-z0-9-]+/(a2a\|\.well-known/agent-card\.json)(/\|$)` | regex | the balancer itself |
| `/lb-health` | exact | the balancer itself |
| `/api/coverage/runs/agent` | exact | `api` |
| `/copilotkit/` | prefix | `copilot-runtime` |
| `/api/chat` | exact | `api` |
| `/api/` | prefix | `api` |
| `/dev/` | prefix | `api` |
| `/.well-known/agent-card.json` | exact | `api` |
| `/a2a` | prefix | `api` |
| `/mcp` | exact | `mcp-retrieval` |
| `/portfolio/mcp` | exact | `mcp-portfolio` at `/mcp` |
| `/compliance` | prefix | `compliance` |
| `/v1/traces` | prefix | `otel-collector` |
| `/jaeger` | prefix | `jaeger` |
| `/` | prefix | `web` |
| `/example/mcp` | exact | `mcp-example` at `/mcp` (plugin `_example`) |
| `/code/mcp` | exact | `mcp-code` at `/mcp` (plugin `code`) |
<!-- /generated:lb-routes -->

<table>
<tr>
<td width="50%" valign="top">
<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/screenshots/jev-dark.png">
  <img src="docs/screenshots/jev-light.png" alt="Jev statistics: requests, availability and latency per call site">
</picture>

**Jev** (`/admin/jev`): every call TypeSafe's Jev made for the firm's turns, and how often it was unavailable
</td>
<td width="50%" valign="top">
<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/screenshots/evals-dark.png">
  <img src="docs/screenshots/evals-light.png" alt="Eval runs: the retrieval trend against its baseline and every suite's metrics">
</picture>

**Evals** (`/evals`): each suite's metrics over time, against the accepted baseline
</td>
</tr>
</table>

The same picture, drawn in [`docs/topology.drawio`](docs/topology.drawio) and **live**, is at
[`/topology`](http://localhost:7171/topology): each box carries the state of that service — healthy, degraded or
unreachable — its replicas by name, and the facts that explain the lab's behaviour (chunks in the index, models,
tools offered). Edit the diagram in draw.io (save it *uncompressed*) and the page follows; a service that exists in
the report but not in the drawing fails the test suite.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/screenshots/topology-dark.png">
  <img src="docs/screenshots/topology-light.png" alt="The live topology: every service of the stack with its health and replicas">
</picture>

## Quick start

```bash
make setup                 # install what's missing (.NET SDK at the version global.json pins, into ~/.dotnet)
export OLLAMA_API_KEY=…    # chat runs on Ollama Cloud (gpt-oss:120b); the key is only read from the environment
export JEV_MAF_LAB=…       # intent classification runs on TypeSafe Jev; same rule
make                       # doctor-lite → build → start → wait until healthy → index if empty → http://localhost:7171
make core                  # the core alone: no plugin, no built-in domain; every turn declines (plain make brings them back)
make help                  # every target
```

<!-- generated:make-targets — edit the Makefile's ## comments, then run make docs -->
| Command | What it does |
|---|---|
| `make all` | Start everything: build, run, wait for health, index if empty (default) |
| `make help` | List the targets |
| `make up` | Build and start the stack (replicas via API_REPLICAS/MCP_REPLICAS/PORTFOLIO_REPLICAS/COMPLIANCE_REPLICAS), wait until healthy |
| `make core` | Start the core with no plugin and no built-in domain (MAF_PLUGINS=none); declines every turn (decision 5h); a plain make brings them back |
| `make product-check` | Build the product image variant (api, web) and check it holds no dev-or-qa-only plugin code |
| `make plugins` | List every plugin: kind, scope, environments, whether installed, dependencies, description |
| `make plugin-new` | Start a new plugin (NAME=…, KIND=mcp\|app): mcp copies _example, app renders the app template; prints the files written |
| `make plugin-new-check` | Scaffold one plugin of each kind, build them, check the docs, then remove them (CI) |
| `make plugin-on` | Install one plugin into the running stack (NAME=…): its services, healthy, then its routes; ALLOW_DOWNTIME=1 for one api replica |
| `make plugin-off` | Remove one plugin from the running stack (NAME=…); refuses while it has open work unless STOP_WORK=1 |
| `make down` | Stop the stack (data volumes are kept) |
| `make restart` | Stop and start the stack |
| `make ps` | Show services, state and health |
| `make logs` | Follow logs (SERVICE=api to narrow) |
| `make clean` | Remove the stack WITH volumes (index, conversations) and build outputs; asks unless FORCE=1 |
| `make infra` | Start only the indexer's infrastructure (Qdrant, Neo4j, both Ollama instances + the embedding model) and wait until healthy |
| `make index` | Index the built-in domains' corpora, then build their graph; installed plugins add theirs (unchanged documents are skipped) |
| `make graph` | Build the Neo4j graph: billing relationships, and any installed plugin's graph (unchanged nodes are not rewritten) |
| `make neo4j-chunks` | Spike: copy the billing and portfolio chunks from Qdrant into Neo4j for eval-retrieval-backends |
| `make index-portfolio` | Index the portfolio corpus (data-portfolio/ → maf_portfolio_chunks) only |
| `make reindex` | Re-embed every document of the built-in domains, and of installed plugins (--force) |
| `make drift` | Report stale documents: the index and the billing graph against the source |
| `make rebuild-index` | Re-create the collection with every configured dense vector and re-index (asks unless FORCE=1) |
| `make migrate` | Fill a provisioned dense vector with its configured model (TO=dense_v3) |
| `make test` | Run all tests (.NET unit + integration, web) |
| `make test-dotnet` | .NET tests (integration tests start Qdrant and Neo4j via Testcontainers) |
| `make test-web` | Web tests (Vitest) |
| `make lint` | Build .NET with warnings as errors; ESLint + Prettier for web |
| `make lint-dotnet` | .NET build with warnings as errors |
| `make lint-web` | ESLint + Prettier |
| `make build-web` | Type-check and build the web app |
| `make specs` | Validate all OpenSpec specs and changes (strict) |
| `make docs` | Rewrite the generated blocks in README, project.md, config.yaml and the Copilot instructions |
| `make docs-check` | Check the docs against the code (generated blocks, routes, make targets, models, links); changes nothing |
| `make ci` | Run locally what GitHub Actions runs on every pull request |
| `make ci-e2e` | Model-free end-to-end: stack with the Ollama stub, index, verify, A2A conformance, test generation (CI mode) |
| `make ci-e2e-core` | Model-free core-only end-to-end: make core with the Ollama stub, verify, and the decline with no model, Jev or tool call (CI mode) |
| `make core-turn-check` | On a core-only stack: a turn declines with the fixed reply, and the stub saw no model or Jev call (used by ci-e2e-core) |
| `make testgen-e2e` | Model-free test generation end to end: refresh, run, verify, accept (used by ci-e2e, against its clone) |
| `make coverage` | Refresh the coverage snapshot at main (both toolchains, through the running stack) |
| `make verify` | Verify the running stack through the load balancer (37 checks), then AG-UI conformance of every agent (8 checks) |
| `make eval` | Run evals (SUITE=all\|selection\|retrieval\|generation\|injection\|confirmation\|intent\|domain\|presentation\|guardrail\|answer-check\|code-route\|graph-depth\|generation-judge) against the stack's MCP servers |
| `make ask` | Ask one question through the agent and print its trace (Q="…" TENANT=firm-a), e.g. a cross-domain one |
| `make screenshots` | Re-take the README screenshots from the running stack into docs/screenshots (SHOTS=chat,topology for a subset) |
| `make eval-accept` | Run the evals and accept their metrics as the new baseline (REPEAT=N: mean of N runs; commit the result) |
| `make eval-selection` | Eval: tool selection (recall/precision) |
| `make eval-retrieval` | Eval: retrieval (recall@5/@20, MRR per mode) |
| `make eval-generation` | Eval: answers graded by Jev, mean of 3 runs (REPEAT=N to change) |
| `make eval-injection` | Eval: prompt-injection pass rate |
| `make eval-confirmation` | Eval: does the summary a person approves say what would happen |
| `make eval-intent` | Eval: intent classifier alone — would each question force search_documents? (needs JEV_MAF_LAB) |
| `make eval-guardrail` | Eval: content guard alone — are malicious prompts/tool results flagged and benign ones not? (needs JEV_MAF_LAB) |
| `make eval-presentation` | Eval: do portfolio answers build on their data cards instead of restating them? |
| `make eval-answer-check` | Eval: Jev's answer check alone — are labelled unsupported answers flagged and supported ones not? (needs JEV_MAF_LAB) |
| `make eval-code-route` | Eval: Jev's code-route answer alone — would each code question start with the right graph call or the search? (needs JEV_MAF_LAB) |
| `make eval-retrieval-backends` | Spike comparison: retrieval cases on Qdrant and on Neo4j side by side, never gated (run make neo4j-chunks first) |
| `make eval-graph-depth` | Comparison: code graph traces at depth 2, 3 and 4, side by side, never gated (STRUCTURAL=1 for no model) |
| `make eval-a2a` | Conformance: an outside client drives the agents through evals/a2a-conformance.jsonl |
| `make dev` | Run mcp/api/web locally without Docker (infra stays in compose); Ctrl-C stops |
| `make doctor` | Check prerequisites (Docker, .NET SDK, Node/npm, make, OLLAMA_API_KEY, JEV_MAF_LAB, MAF_LAB_REPO, GITHUB_ISSUES_TOKEN) |
| `make setup` | Install what 'make doctor' reports missing (.NET SDK unattended; prints the rest) |
| `make index-code` | Index the repository itself (→ maf_code_chunks, served by mcp-code) only; unchanged files are skipped |
| `make graph-code` | Build the code graph only (calls, types, tests) in Neo4j |
<!-- /generated:make-targets -->

## Plugins

Everything optional is a plugin: one folder `plugins/<name>/` with a `plugin.toml` manifest (checked against
`plugins/plugin.schema.json`). `MAF_PLUGINS` decides what `make` installs: unset means every bundled plugin `MAF_ENV`
allows, `none` means the core alone (`make core`), otherwise a comma-separated list. `make plugins` lists them,
`make plugin-on NAME=…` and `make plugin-off NAME=…` switch one on the running stack. The authoring guide is
[docs/plugins.md](docs/plugins.md).

<!-- generated:plugins — edit plugins/<name>/plugin.toml, then run make docs -->
| Plugin | Kind | Scope | Environments | What it is |
|---|---|---|---|---|
| `_example` | mcp | tenant | dev, qa | The authoring template: a small MCP server in its own container with one tool, get_example_fact, and a domain descriptor that routes questions about the sample fact to it. Off unless asked for. |
| `a2a-inspector` | infra | installation | dev, qa | The A2A Inspector (a2aproject), opened on the lab's agent cards with a fresh partner token: a dev and qa tool. |
| `code` | mcp | installation | dev, qa | The lab's own source code as a domain: the codebase MCP server (search_codebase and the code-graph tools), its domain descriptor and routing, and the chat's Code snippets pane. |
| `conversation-history` | app | installation | dev, qa, stage, prod | The chat's conversation list: the caller's own conversations, searched and paged, renamed and deleted, beside the chat. Without it, a conversation is still reopened by its URL and a new one started from the chat's header. |
| `mcp-inspector` | infra | installation | dev, qa | The MCP Inspector, listing the lab's MCP servers with a dev user's token: a dev and qa tool. |
| `monitor` | app | installation | dev, qa | Behind the scenes of every chat turn: the full trace (model calls, prompt, retrieval diagnostics, guard, answer check), live while it runs and kept for a while after, with the run's AG-UI frames and time travel. |
| `neo4j-browser` | infra | installation | dev, qa | Neo4j Browser on the graph store, forwarded on loopback: a dev and qa tool. |
| `redis-insight` | infra | installation | dev, qa | Redis Insight on the lab's Redis (run state, stops, shared stores), loopback only: a dev and qa tool. |
<!-- /generated:plugins -->

## Chat history

With the `conversation-history` plugin (installed by default), the left sidebar of `/chat` lists your own conversations, most recent activity first. You can search titles, questions
and answers. Open a conversation to restore every turn exactly as it looked (answers, tool cards, sources, feedback,
and the behind-the-scenes trace with time travel while it is kept), then continue it. The active conversation is in
the URL (`/chat/{id}`), so a reload reopens it. Rename or delete from the item's menu. Delete hides the conversation
and stops it being continued; its turns stay for the review queue and evals. Without the plugin there is no sidebar: a
conversation is reopened by its URL, and "New conversation" is in the chat's header either way.

## Asking about the code

With the `code` plugin (dev and qa), the repository itself is a corpus, indexed by `make index` while the plugin is
present. It is indexed by structure: each type and member with its doc
comment, sized under embeddinggemma's 2048-token window, and searchable by identifier as well as by meaning. Ask in the
chat, "how does the code make a tool call idempotent?" or "покажи ми дефиницията на code mcp сървъра". Jev puts the
question in the codebase domain, the turn loads the codebase server's `search_codebase` (plus `trace_code_symbol` and
`change_impact` for who-calls and what-tests-cover questions), and the answer cites
`path:start-end`. The right pane switches to **Code snippets**, which shows exactly the snippets the answer used. For
a question that did not search the code, the tab shows related code, labelled as not used. Other MCP clients can call
`search_codebase` and `ask_codebase` at `http://localhost:7171/code/mcp` with a dev token.

The repository is also a graph (`make graph`, also run by `make index`), built with Roslyn so a call edge is the method
the compiler binds. "Who calls TenantScopedSearch.QueryAsync?" goes to `trace_code_symbol`, and "what tests cover
src/Maf.Lab.Retrieval/Store/TenantScopedSearch.cs?" goes to `change_impact`, which follows callers four calls deep and
groups the tests it reaches by file. The billing side of the same graph links firms, households, accounts, billing runs,
fee schedules and documents: "which households use fee schedule NW-INST-2026-083?" goes to
`trace_billing_relationships`, which returns the documents by the same id `search_documents` uses. Both graphs are read
only through one tenant-scoped method that runs a fixed set of Cypher queries, so the model never writes a query.
In the monitor, each graph tool call has its own `graph` row in the timeline, for example "Neo4j billing_neighbourhood_2 +
firm_runs · 9 rows · 12 ms". The Retrieval view adds a card with each template's rows and a `neo4j` timing chip, next to
the searches' `qdrant` chips. It shows structure only, and the call's arguments stay in its `tool.call`.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/screenshots/code-snippets-dark.png">
  <img src="docs/screenshots/code-snippets-light.png" alt="A question about the code, answered with path:line citations, and the Code snippets pane showing the snippets the answer used">
</picture>

## Asking in another language

The corpus is English. A question written in another language is translated into the corpus language *before* it is
embedded and before BM25 encodes it, so both halves of hybrid retrieval work on the same vocabulary as the index;
the answer still comes back in the language of the question. The monitor's Retrieval tab shows both texts, and the
retrieval eval reports recall per language. When translation was introduced, Bulgarian recall@5 over the eval set went
from **0.21 to 0.68** while English stayed at 0.69; today's accepted figures are in `evals/baseline.json`. Switch it
off with `Retrieval__NormalizeQueryLanguage=false`; the corpus language is `Retrieval__CorpusLanguage` (default `en`).

## Another agent talking to this one (A2A)

The assistant is also an **[A2A 1.0](https://a2a-protocol.org) agent**, so another system can work with it without
a person in the loop. It advertises itself at `http://localhost:7171/.well-known/agent-card.json` — a signed card
naming its skills, its transports (JSON-RPC and HTTP+JSON) and how to authenticate — and answers on `/a2a`.

A partner is a **system, not a user**: its token's audience is the A2A endpoint, it carries no user identity, and
the firms it may see come from the server's `A2A:Partners` registration rather than from anything it sends. Ask
about a firm outside that set and you get one fixed sentence and no data — not the run, not whether it exists.

It can ask about a billing run, ask anything else (answered by the same agent and the same MCP tools the chat UI
uses), or **start a billing run** — which comes back as a task it can follow, resume when the agent asks for a
missing period, resubscribe to after a dropped connection, cancel, or be notified about through a webhook. That
run is **simulated**: it walks the real lifecycle over the seeded runs and bills nobody. The card says so, the
final artifact says so (`"simulated": true`), and so does this paragraph.

Two clients prove it from outside: `python3 scripts/a2a_probe.py` speaks the 1.0 wire format and imports no A2A
library at all, and `make eval-a2a` runs `tools/Maf.Lab.A2AProbe`, which builds an agent from nothing but the card
using the A2A client. The preview SDK underneath does not yet speak 1.0 on the wire; every difference and what is
done about it is in `DECISIONS.md`.

**Conformance is a dataset, run by an outside client.** `evals/a2a-conformance.jsonl` names what such a client
must be able to do — discovery, the extended card after authenticating, a direct answer, a streamed task,
resubscribing after a dropped stream, resuming a task that asked for something, cancelling, a push delivery to a
webhook it registered, and a request outside its entitlement being refused. The probe reads that file and runs
the scenario each row names; it has no project reference to `src/`, which is what makes it evidence rather than a
self-assessment. It writes a report in the same shape every eval suite writes, to `evals/reports/`, and
`make ci-e2e` runs it. `make eval SUITE=…` does **not** — that target dispatches into the harness, which links
against the service.

**`/admin/a2a`** (TENANT_ADMIN) is where that traffic is visible: what arrived from partners, what this system
asked of the reviewer, and every push delivery — each with its state, when it happened and how long it took. A
task still running can be cancelled from there, through the same protocol call a partner would make. The same
page shows the test-generation agent, through the api: whether its card answers, what the card says, what a run
gets by default, and its runs by state with the most recent ones — each with how long its work took, counting on
while it runs, and what its model calls cost (the amount its budget counted, `≈` when the price is an estimate) —
each file opening on the Coverage page.

## A second agent it consults (A2A, the other way round)

The lab runs a **second agent** of its own: a compliance reviewer, in its own container behind the same entry
point at `/compliance`, with its own card, its own audience and its own credentials. Its single skill is to review
a proposed fee adjustment and return a verdict — and it behaves like a real dependency rather than a function
call: it takes tens of seconds, reports progress while it works, and about one review in five stops and asks for
the advisor's justification before deciding.

The assistant consults it as a **sub-agent** through the Agent Framework's A2A client: it fetches the reviewer's
card, turns it into an agent, and authenticates **as itself** — a user's token is never forwarded, and the
reviewer never learns who asked. Every way the consultation can end is a value the caller must handle: a verdict,
a question back, a timeout that keeps the task id so the answer can be collected later, an unreachable agent, or a
failure. Each one is written to the same audit record as everything else, with no message content.

That verdict is **simulated** — a threshold and a stopwatch. It binds nobody, and the card, the artifact and this
paragraph all say so. `make eval-a2a` drives both agents from outside, using nothing but their cards.

A verdict is another system's word about our question, so it is checked before it is believed: it must decide,
and it must decide about the adjustment and the account we asked about. `evals/injection-a2a.jsonl` holds what a
broken or hostile reviewer might send — an instruction buried in its reason, a verdict about a different account,
a decision that is neither approved nor refused — and each one is driven through that check and through the write
flow against a reviewer that answers with it verbatim. None of them changes what executes.

## Coverage, and a third agent that writes tests

**`/coverage`** shows how well the lab's own C# and TypeScript source is covered by tests — folder by folder, file by
file and line by line, each file against its threshold (a configured default, or the file's own override). Any
signed-in user can look; only a TENANT_ADMIN can change a threshold, refresh coverage or start, cancel, accept or
discard a run, and the server enforces that. `make coverage` refreshes the snapshot at `main` through the running
stack.

Raising a file's threshold above its current coverage asks to confirm, then for a model and its cost estimate, and
then starts a **test-generation run**. The api hands the file to the **test-agent**, a third agent reachable only over
A2A and only by the api. It reads the repository at the run's commit, may write only test files, and loops — write
tests, run them with coverage, read the result — until the file reaches the target or it runs out of attempts. The
tests run in the **coverage-runner**, which has no secrets and no route to the internet. A test the agent believes
exposes a bug is skipped and reported, not worked around.

The api then checks the result itself before anyone sees it, commits it to a candidate branch
(`test-agent/<file-slug>-<runId>`), and waits for a TENANT_ADMIN to accept it — a conflict-free merge into `main` — or
discard it. A suspected bug whose test still fails when un-skipped becomes a GitHub issue (`GITHUB_ISSUES_TOKEN`). The page
follows a run live, and `/admin/a2a` shows the agent and its runs. `make testgen-e2e` drives the whole path without a
model; `make ci-e2e` includes it.

The api writes to the repository (those branches and merges), `data/` and `evals/` as the user who ran `make`
(`MAF_LAB_UID`/`MAF_LAB_GID`, from `id -u`/`id -g`), so on Linux everything it leaves there is yours; a one-shot
`api-data-init` hands its data volume to that user first. Earlier versions ran it as root: `make up` finds paths owned
by root in the checkout and gives exactly those back to you. On rootless Docker or `userns-remap`, run
`make MAF_LAB_UID=0 MAF_LAB_GID=0`.

## The first thing it can change

Everything else the assistant does can be undone by closing the tab. `propose_fee_adjustment` is the exception —
and it cannot use itself. Asking for a fee to be adjusted gets you a **proposal**, never a change: the tool
returns the account, its current fee, the amount, the fee that would result and the period it would affect, and
the turn ends there. The adjustment happens only when the advisor answers, and only then.

What the advisor confirms is what executes. The proposal travels back as an opaque, signed token, and the second
call runs *that* rather than whatever the model repeated in the meantime — point it at another account and the
original one is still what moves. Approving twice charges once: the ledger's unique key refuses the second
application and reports the first one's result. Nothing is stored until something is actually applied; the seeded
accounts stay read-only, and an account's current fee is the seed plus its applied adjustments.

Above a configured amount (`FeeAdjustments:ReviewAboveAmount`, 500 by default) the compliance reviewer sees it
before the advisor does. Its refusal ends the flow, its question is put to the advisor and answered under the
same review, and a timeout, an unreachable reviewer or a failure ends the flow with an explanation — never with a
confirmation, and never with a write. Its verdict is treated as coming from a system this one does not control:
checked against the account and adjustment that were sent, and handed to the model as data. An instruction
embedded in it changes nothing, because nothing reads it for instructions.

Every step — proposed, reviewed, confirmed, rejected, applied — is in the audit record under `fee.adjustment`,
naming the person who acted and the account, and never the reason they gave.

In the browser the proposal is a card in the conversation, not a dialog over it: the account, the fee now, the
change, the fee after, the period, and Approve or Reject. It says when it stops being answerable and stops
offering the buttons once it has. Close the tab and come back and the card is still there — the run is gone, but
the proposal is a row, and approving twice applies once. A fourth feedback button says what none of the other
three could: this summary reads correctly and describes the wrong adjustment. What it reports is measured by its
own eval suite, which checks that the sentence states the account, the amount and the fee that would result.

## What the browser and the API speak

A turn is a **run of the agent**, streamed as [AG-UI](https://github.com/ag-ui-protocol/ag-ui) — a protocol
someone else defined, with a stable 1.0 schema and official SDKs on both sides — and **nothing but AG-UI**: only the
protocol's own event types travel, built only by its official libraries. On the server every agent is a Microsoft Agent
Framework `AIAgent` behind the Agent Framework's own AG-UI server (`MapAGUIServer`); what this system adds is content
the server's registered mappings turn into the protocol's events — data cards as activities, the account in focus as
shared state, what the turn is doing as steps, a write waiting for a person as an interrupt. The sources of an answer
travel in its search's own tool result, and the behind-the-scenes trace never travels on the stream: the monitor reads
it from a trace API while the run is live. In the browser, CopilotKit (headless, through its own runtime) is the only
way to an agent, and the screens render the protocol's events — so an agent can be swapped behind a screen, or a
screen behind an agent, without the other changing. An architecture test and lint rules fail the build on a custom
event, an event built outside the mappings, or stream code of our own; `make verify` drives every agent with a bare
AG-UI client (DECISIONS §74).

Arguments and results are identifiers and summaries on the wire, never the words a user typed or the documents a
tool found. The official adapter attaches the whole originating chat update to every event; the server drops it, and
redacts calls and results before the adapter sees them — the sort of thing worth checking rather than assuming.

A write waiting for a person is the protocol's own **interrupt**: the run pauses carrying what to check, the
shape of the answer and when the proposal expires. Approving or rejecting is a new run that resumes it. There is
no separate confirm endpoint, because the protocol already had somewhere to put this.

## Behind the scenes

With the `monitor` plugin (dev and qa), `/chat` shows the conversation on the left and a live **behind-the-scenes
monitor** on the right: every step of the turn
as it happens. Tabs:
- **Timeline:** a waterfall of every step.
- **Model:** each request (messages, tools, tool mode) and response (text, tool calls, tokens, latency).
- **Retrieval:** tenant scope, settings, BM25 terms with IDF, and the dense, sparse and fused candidates side by side.
- **MCP:** raw arguments and results, plus the api and mcp replica that served them.
- **Domains:** Jev's probability per domain against the scope floor, the path across servers call by call, each
  boundary crossing, and whether the calls went where Jev predicted.
- **Prompt & memory:** system prompt, tool schemas and the history window.

**Intent:** before the first model call the question is classified, which decides whether the turn is *forced* to call
`search_documents`. Every question, in any language, is classified by TypeSafe's Jev (`jev-1.13.0`, key from
`JEV_MAF_LAB`), which returns one of the five intents with a probability for each and a confidence, and — in the same
call — the probability that the question is about fee billing at all. Retrieval is forced only for a procedural
question inside the domain (`Jev:MinInDomain`, 0.2), so "how do I cook carbonara?" is not sent to the documentation.
The intent event shows all of it, e.g. "Intent Other (jev 0.99, outside the domain 0.00, 282 ms)".

**Domains:** the same Jev request asks, per domain, whether the question belongs to it: billing (`in_domain`),
portfolio (`in_portfolio`) and the codebase (`in_codebase`). A turn loads only the tools of its domains in scope. A
follow-up that Jev puts in no domain keeps its conversation's domains, and with no verdict every server is loaded. A domain at or above `Jev:MinDomainScope` (0.5) is in scope, and two in scope means the
question **crosses** the boundary: a procedural question then searches both domains' documentation, each on its own
server, before the model's first call. The trace records a `domain` event (the verdict), `domain`/`server` on every tool
event, a `boundary` event whenever a call enters the other domain, and the domain path on `turn.end`; the monitor's
**Domains** tab draws it, and `make eval SUITE=domain` measures the verdict over 81 labelled questions. Try "Why did
the fee on A-1042 go up this quarter — did its AUM cross a tier?" as firm-a. `make eval-intent`
measures the classifier alone over 101 labelled questions in English, Bulgarian and Latin-script Bulgarian. A choice below `Jev:MinConfidence` (0.5), a timeout
(`Jev:TimeoutSeconds`, 2; 0 disables it), a rejected call or a missing key leaves the turn unforced, and the event
says why.

**Reaching Jev:** these calls all go through one HTTP client per process: classification, screening, the relevance
judge and the answer check. The client keeps its connection alive between requests and replaces it after
`Jev:PooledConnectionLifetimeMinutes` (10), so a DNS change is still picked up.
- **Warm-up:** once a service has started, it sends one fixed-text request (`Jev:WarmUp`, on; `Jev:WarmUpTimeoutSeconds`, 5).
  This way the first turn does not pay for TLS set-up.
  - Every api, mcp-retrieval, mcp-portfolio and mcp-code replica does this.
  - It runs in the background, is skipped without a key, and is not counted in the Jev statistics.
- **Retries:** a transient failure is retried `Jev:MaxRetries` (1) times, after `Jev:RetryDelayMs` (100, doubled per
  retry) or the server's `Retry-After`. A transient failure is no response, 408, 429 or a 5xx.
  - A retry never outlasts the caller's timeout.
  - Other 4xx statuses are not retried. `Jev__MaxRetries=0` turns retrying off.
- **Circuit breaker:** after `Jev:Breaker:FailureThreshold` (3) consecutive timeouts, transport errors or final
  408/429/5xx, a process skips Jev for `Jev:Breaker:OpenSeconds` (30). Every call then returns at once with reason
  `circuit open`, sends nothing, and each site does what it does whenever Jev is unavailable.
  - After the period one real call goes through as a probe: success closes the circuit, failure opens it again.
  - A missing key, a 401/422 or a caller's own cancellation never counts. Each replica has its own breaker.
  - `/admin/jev` counts the skipped calls apart from requests. `Jev__Breaker__FailureThreshold=0` turns it off.
- **Logs:** every attempt is logged with its status code and duration, e.g. `Jev attempt 1/2 → 429 in 180 ms`, then
  `Jev retrying after 429 in 104 ms (attempt 2/2)`, and the warm-up logs `Jev warm-up answered by jev-1.13.0 in 412 ms`.
  - The breaker logs only its state changes, e.g. `Jev circuit opened after 3 consecutive failures (last: timed out
    after 2s); skipping Jev for 30s`, `Jev circuit half-open: probing`, `Jev circuit closed after 57214 ms; 22 calls
    skipped`.
  - Watch them with `make logs SERVICE=api | grep Jev`.
  - The lines hold numbers and type names only: no question, no passage, no key.

**Time travel:** scrub, step (←/→), jump (Home/End) or replay (Space; 1×–10×, long waits compressed) through any
turn. Every tab shows the state as of the chosen step, and the chat rewinds with it: the answer text, tool cards and
sources appear as they were at that moment. While a turn streams, the monitor follows it; drag back to pause and use
"Back to live" to catch up.

Click an earlier answer to reopen its stored trace (kept 7 days). Reviewers can open a trace from `/admin/feedback`. The
event format is in [`docs/trace-events.md`](docs/trace-events.md). Retrieval internals come from the MCP server in the
tool result `_meta`, which the model never sees.

## Developer tools

The inspectors — A2A Inspector, MCP Inspector, Redis Insight and Neo4j Browser — are plugins allowed in dev and qa
only (listed in the plugins block above). Installed, each runs on this machine only (published on `127.0.0.1`), opens
ready to use, and is linked from the web UI's navigation; CI's plugin set leaves them out. What each one opens with is
in the README.md of its plugin folder.

## Local development (without the balancer)

`make dev` bypasses the balancer: web on :5174 (Vite proxies `/api` and `/dev` to :5080), api on :5080, MCP servers on :5090 (billing), :5091 (portfolio) and :5092 (codebase),
with Qdrant, Neo4j and both Ollama instances from compose (the compose app services are stopped first); query
embeddings go to :11435 and document embeddings to :11436 (`Models__BatchOllamaEndpoint`). The manual equivalent:

Prerequisites: .NET SDK 10.0.401 (`global.json`), Node 24, Docker, Ollama (host or compose).

```bash
docker compose -f compose/docker-compose.yml up -d qdrant     # vector store only
ollama pull embeddinggemma                                    # (qwen3:4b only for the local chat fallback, see DECISIONS.md)
                                                              # one host Ollama serves queries and documents alike:
                                                              # leave Models__BatchOllamaEndpoint unset

dotnet run --project src/Maf.Lab.Indexing                     # index data/ (index | drift | status | rebuild --yes | migrate --to <vector>)
dotnet run --project src/Maf.Lab.Retrieval                    # billing MCP server on :5090
dotnet run --project src/Maf.Lab.Portfolio                    # portfolio MCP server on :5091
dotnet run --project src/Maf.Lab.CodeSearch                   # codebase MCP server on :5092 (make index first)
dotnet run --project src/Maf.Lab.Api                          # agent host on :5080
cd web && npm install && npm run dev                          # UI on :5174
```

VS Code: the compound launch **"api + web (with mcp-retrieval and mcp-portfolio)"** starts all four (not the codebase server: run `src/Maf.Lab.CodeSearch` by hand); tasks cover `compose up`, `index`,
`eval` and tests.

Switch the model provider by configuration only, e.g. `Models__Provider=openai Models__OpenAIApiKey=… Models__ChatModel=gpt-4.1-mini`
(Azure OpenAI: also set `Models__OpenAIEndpoint=https://<resource>.openai.azure.com/openai/v1/`). Embedding models live
under `Models:Embeddings` (one entry per Qdrant named vector).

## Tests

```bash
make test        # dotnet test --solution maf-lab.sln (Testcontainers starts qdrant/qdrant:v1.19.1 and neo4j:2026.09.0-community) + web Vitest
make lint        # .NET build with warnings as errors + ESLint/Prettier
make docs-check  # scripts/docs.py's unit tests, then the docs-vs-code check (Python only)
```

Tests never call a model: they use a deterministic feature-hashing embedder and a scripted chat client. Evals are
separate and do call the model.

## Continuous integration

GitHub Actions ([`.github/workflows`](.github/workflows)) — `make ci` runs the same checks locally.

| Workflow | Trigger | What runs |
|---|---|---|
| **CI** (`ci.yml`) | every pull request, every push to `main`, and on demand | `specs` (OpenSpec strict validation and `make docs-check`) · `dotnet` (build with warnings as errors, unit + Testcontainers integration tests) · `web` (lint, Vitest, build) · `e2e` (full stack behind the balancer on :7171, corpus indexed, `make verify`, the A2A conformance probe and model-free test generation: `make ci-e2e`) |
| **Evals** (`evals.yml`) | manual (*Actions → Evals → Run workflow*, choose a suite) | real embeddings in compose Ollama + chat on Ollama Cloud and intent on Jev (`OLLAMA_API_KEY` and `JEV_MAF_LAB` repository secrets); reports uploaded as an artifact |

The e2e job needs **no model and no secret**, so it also runs for pull requests from forks. `CI_MODE=1` replaces both
Ollama services (`ollama`, `ollama-batch`) with a deterministic Ollama-compatible stub (`compose/ollama-stub`): hash-based embeddings and a
scripted, streamed chat answer. Forced retrieval still calls `search_documents` over MCP, so tool calls, SSE, sources,
tenancy, failover and admin jobs are exercised for real. Try it locally: `make ci-e2e` (indexing takes about 15 s with the stub).
Locally it runs as its own compose project, `maf-lab-e2e`, with its own volumes: it stops the dev stack first (its
data is kept, the ports are shared), removes itself when it passes and stays up for inspection when it fails. Run
`make` afterwards to bring the dev stack back.

```bash
gh workflow run evals.yml -f suite=selection   # dispatch evals from the CLI
gh run watch                                   # follow it
```

## Keeping docs in sync

Facts that the code already holds are not written by hand. `make docs` writes them into the documents, between
`generated:` markers:

| Block | Where | Source |
|---|---|---|
| `make-targets` | this README | the Makefile's `##` target descriptions |
| `lb-routes` | this README, [Copilot instructions](.github/copilot-instructions.md) | [`compose/lb/nginx.conf`](compose/lb/nginx.conf) |
| `repo-layout` | [`openspec/project.md`](openspec/project.md) | each `.csproj` `<Description>`, and `[layout]` in [`docs/docs-sync.toml`](docs/docs-sync.toml) |
| `project-context` | [`openspec/config.yaml`](openspec/config.yaml) | the whole of `openspec/project.md` |

Agents do not have to remember `make docs`. A Claude Code hook in [`.claude/settings.json`](.claude/settings.json)
runs [`scripts/docs_hook.sh`](scripts/docs_hook.sh) after every edit to one of these sources (Makefile, `nginx.conf`,
a `.csproj`, `project.md`, `docs-sync.toml`). It regenerates the blocks in that checkout and tells the agent which
files changed. It never blocks: CI is the gate.

`make docs-check` changes nothing. It fails, naming file and line, when:
- a generated block is out of date;
- an api route has no row in [`docs/http-api.md`](docs/http-api.md), or a row names a route that is not registered;
- a document runs a `make` target that does not exist;
- a document names a chat, embedding or Jev model other than the configured one;
- a relative link is broken;
- a page the web app registers (`web/src/App.tsx`) is not named in this README as `` `/path` ``;
- an active OpenSpec change has no `## Documentation impact` section.

It runs in `make ci` and in the CI `specs` job, with Python 3.11+ and nothing else. Deliberate exceptions live in
[`docs/docs-sync.toml`](docs/docs-sync.toml), each with its reason:
- a route left out of the API reference;
- a route an SDK registers;
- a route another host serves behind the same balancer;
- a model name that is not the default.

`DECISIONS.md`, eval reports and archived changes are history, and are not checked. Prose is checked by review:
archiving a change asks for a read-only pass over these documents against the change's diff.

## Evals — when you must run them

```bash
make eval                     # all gated suites against the running stack's MCP (graph-depth runs only when named)
make eval-selection           # or eval-retrieval / -generation / -injection / -confirmation / -intent / -guardrail / -answer-check / -code-route / -presentation / -graph-depth / -retrieval-backends / -a2a
dotnet run --project src/Maf.Lab.Eval -- --suite retrieval --rerank   # extra flags: use the CLI directly
dotnet run --project src/Maf.Lab.Eval -- --import-feedback --suite retrieval
```

Evals run **on demand**, not on every commit. They are **required** before merging any change to:

- the **system prompt** (`src/Maf.Lab.Api/Prompts/*`) → `selection`, `generation`, `injection`
- a **tool description or schema** (`src/Maf.Lab.{Retrieval,Portfolio,CodeSearch}/Tools/*`) → `selection`
- the **model** (chat or embedding, `Models:*`) → `all`
- the **tool set** (adding/removing a tool) → `selection`, `injection`
- the **chunking or retrieval configuration** (chunkers, `Indexing:*`, `Retrieval:*`, BM25) → `retrieval`, `generation`
- **query normalisation** (`Retrieval:NormalizeQueryLanguage`, `Retrieval:CorpusLanguage`, the translation model) → `retrieval`
- a **Jev screening or check** (the guard's batteries or `Guard:*`, the answer check's questions or `Jev:AnswerCheck:*`) →
  `guardrail`, `answer-check`; both call Jev alone, with no chat model and no tool
- the **generation grade** (`src/Maf.Lab.Eval/Judging/*`, `Evals:Judge:*`) → `generation-judge` first (labelled answers,
  no agent), then `generation`
- the **code graph** (the builder in `src/Maf.Lab.Indexing/Graph`, or a trace or impact depth in `CodeGraphTools`) →
  `graph-depth`
- a question about the **vector store itself** (Qdrant versus Neo4j) → `make neo4j-chunks`, then `retrieval-backends`, a
  comparison like `graph-depth`: the retrieval cases on Qdrant and on an eval-only Neo4j search over the same copied
  chunks, side by side, never gated (neo4j-retrieval-spike)
- the **code-route question** (`CodeToolRouter`, `Jev:RouteCodeTools`, `Jev:MinCodeRouteConfidence`) → `code-route`, which
  asks Jev alone whether each code question would start with the right graph call or the search, and `intent`

`graph-depth` is a **comparison**, not a gate. It runs the same labelled code-graph cases with the graph tools pinned to
2, 3 and 4 calls, on a codebase server it starts itself (`CodeSearch:GraphDepthPin`, never set in a deployment). It
reports two layers side by side:
- **structural**, with no model: recall, also split by the depth a case needs, nodes, tokens, truncation and latency;
- **end-to-end**: the Jev grade `generation` uses (faithfulness against what the turn read, relevance), and whether
  the answer names what it needed.

Beside the all-turn end-to-end scores it reports each variant's scores over its own turns that called a graph tool
(`:graph`, with `graphTurns`), and over the cases that called the graph in every variant (`:common`, with
`commonCases`). Read a depth's effect from the `:common` scores, where the three depths answered the same questions
with the graph. It has no thresholds, is never compared with or accepted into the baseline, and `all` does not run it.
`make eval-graph-depth STRUCTURAL=1` runs the structural layer alone, with no chat model or Jev key. End-to-end numbers
vary between runs, so compare two runs before reading a difference as a result.

### How `generation` is graded

Jev grades every answer, in **one request per case**. Code cuts the answer into sentences, and every question is a
yes/no about one item:
- per sentence: does it state a fact, and is that fact supported by what the turn read. A place the sentence cites
  (`path:start-end`, "Section 3 → Step 2") is looked up in code, not asked of Jev: a place no source holds makes the
  sentence unsupported, and a found one is masked before Jev reads the rest;
- per reference point (`referencePoints` in `generation.jsonl`): does the answer state it, and does it contradict it;
- per source: is it on the question's subject;
- once: does the answer address the question.

Code counts the yeses (a Noul is yes at 0.5) into these metrics:
- `faithfulness`: supported claim sentences over claim sentences;
- `relevance`;
- `completeness`: points stated;
- `referenceAgreement`: 1 − points contradicted;
- `retrievalJudged`: sources on the subject;
- `judgeUncertain`: answers in the 0.2–0.8 band, a diagnostic.

A failed case names the unsupported sentences, the cited places no source holds, and the missed or contradicted
points. Without `JEV_MAF_LAB` the suite refuses to run rather than report a grade of nothing.

The agent answers differently on every run, so `make eval-generation` runs the 36 cases **three times** and gates
the mean (`Evals:Repeat`, or `REPEAT=N`). Every run keeps its own report. `make eval-accept SUITE=generation REPEAT=10`
accepts the mean of ten runs as the baseline.

The grade is an evaluator of `Microsoft.Extensions.AI.Evaluation`. Each case is kept under `evals/reports/meai/`, and
every run writes `evals/reports/<runId>.html` beside its JSON and Markdown: every case with its scores, the sentences
behind them, and how they moved over the last ten runs.

`make eval SUITE=generation-judge` measures the grade itself, with no agent, and reports four variants:
- `grade`: the grade on `answer-check.jsonl`'s labelled answers;
- `check`: the production answer check on the same rows;
- `points`: the grade on `generation-judge.jsonl`'s reference points labelled stated or contradicted;
- `sentences`: the grade per sentence on `generation-sentences.jsonl` — claims, support, and citations correct and
  invented, apart.

Each variant reports accuracy overall and per domain, language and split. See DECISIONS.md §79.

### Not getting worse

The thresholds answer "is this usable at all"; the **baseline** answers "is this worse than it was".
`evals/baseline.json` is committed and records the metrics this repository has accepted, per suite and variant.
Every run compares against it and fails when a metric drops by more than the tolerance, naming what moved:

```
✗ REGRESSION retrieval/hybrid mrr: 0.9 → 0.647 (-0.253)
↑ improved retrieval/hybrid recall@5: 0.5 → 0.687 (+0.187)
· within tolerance retrieval/hybrid recall@5:bg: 0.681 → 0.66 (-0.021)
make eval-accept        # run the suites and accept their metrics as the new baseline (then commit it)
```

A run never moves the baseline by itself, and a run below its thresholds is refused rather than blessed. A metric
the baseline does not mention is reported as *new* and one it mentions but the run did not produce as *missing* —
both are how a rename silently switches the gate off. The tolerance is `Evals:RegressionTolerance` (0.02), overridden
per metric in `Evals:RegressionTolerances`, each override carrying what it was measured from: in `retrieval`,
`recall@5:bg` and `recall@20` use 0.025, because a non-English query is translated by a live model and moved about
0.021 over five runs while `recall@5:en` did not move at all; in `generation`, each
tolerance is the range of every 3-run mean over ten runs (faithfulness 0.035), and a `generation-judge` tolerance is
never below one labelled item. `/evals` plots any metric across past runs with the baseline marked.

Datasets are JSONL under `evals/`; reports land in `evals/reports/` (JSON for the `/evals` page, Markdown for humans).
Three of them are not run by the harness: `a2a-conformance.jsonl` is run by the probe (`make eval-a2a`), and
`injection-a2a.jsonl` and `ui-events.jsonl` are fixture sets driven by tests — the first through the verdict check
and the write flow, the second replayed through the browser's reducer. `ui-events.jsonl` holds runs captured from
a running stack by `scripts/capture_ui_events.sh`; re-capture it when what the server emits changes.
Thresholds are configuration (`src/Maf.Lab.Eval/eval.json` → `Evals:Thresholds`); the command exits non-zero when a
suite falls below them. Labeled production feedback (UI → `/admin/feedback`) is appended to the datasets, so the next
run includes it. The contextual-retrieval variant needs a second index:

```bash
Qdrant__Collection=maf_chunks_ctx Qdrant__MetaCollection=maf_chunks_ctx_meta \
  dotnet run --project src/Maf.Lab.Indexing -- index --contextual on
dotnet run --project src/Maf.Lab.Eval -- --suite retrieval --contextual
```

## Compliance

Every audited action — a tool call, a conversation deletion, a compliance export — is one row in a **single ordered
record**, carrying identifiers only and never message content. Each row is chained: its digest covers its own fields
and the previous row's digest, so a changed or removed row can be detected and *named*.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/screenshots/compliance-dark.png">
  <img src="docs/screenshots/compliance-light.png" alt="The compliance screen: the audit chain verified and the action log of the firm">
</picture>

**`/admin/compliance`** (TENANT_ADMIN) shows it: whether the chain is intact — in words, with how many records were
checked and how many predate it — or, when it is broken, which record broke it and that everything before it is
unaffected. Below that, the firm's actions newest first, filterable by person, kind and period. The same endpoints
serve a script:

```bash
GET /api/admin/compliance/verify                      # intact? how many checked? where does it break?
GET /api/admin/compliance/actions?userId=&kind=…      # the record, paged
GET /api/admin/compliance/export?from=&to=[&userId=]  # the package, with a manifest
```

A TENANT_ADMIN can hand an authorised person a package for a period: their tenant's conversations, turns and actions,
including deleted conversations marked as deleted. Adding `userId` narrows it to one person, for a data subject
request. The firm comes from the token, so no parameter reaches another firm. The manifest carries who produced it,
when, the counts, the audit chain head, and a digest over a canonical rendering of the content — documented in
[`docs/http-api.md`](docs/http-api.md) so a recipient can recompute it in any language.

**What this does not promise.** A hash chain is evidence of tampering, not protection from it: whoever can write the
database can recompute the whole chain. Real immutability needs storage the application cannot rewrite (a WORM
bucket, an external log service), which is a deployment decision. And the dev token issuer still decides who "adam"
is — the chain proves what was recorded, not that the recorded person is who they claim.

## Non-negotiables

Tenant comes from the token only · one tenant-scoped query path (and one for the graph, no model-written Cypher) · tool results are DTOs · no message content in logs ·
version moves update `DECISIONS.md` · `generated:` blocks are never edited by hand (`make docs`). See
[`CLAUDE.md`](CLAUDE.md).
