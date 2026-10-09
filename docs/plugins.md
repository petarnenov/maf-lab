# Plugins

The core of maf-lab is a domain-agnostic assistant shell. Everything optional is a plugin: a domain the assistant
answers about, an agent it consults, a screen, a provider, a dev tool. A plugin is **one folder**; deleting the folder
removes it from every part of the system, and `make test` and `make docs-check` stay green. Design:
`openspec/changes/introduce-plugins/design.md` (decisions 1-10, "Principles and patterns"); spec: `plugins`.

## Start one

```bash
make plugin-new NAME=weather KIND=mcp   # a copy of _example under the new name: an MCP server in its own container
make plugin-new NAME=notes KIND=app     # an in-process plugin: one route over a core port, a page, their tests
make docs                               # the plugin's routes, targets and catalogue row reach the generated docs
make plugin-on NAME=weather             # start it on the running stack
```

`plugin-new` prints the files it wrote. It refuses an existing folder, a name that is not a plugin name (lower-case
letters, digits and hyphens, starting with a letter), a leading underscore and an unknown kind, each with one line
saying what to change. It writes into a temporary folder and renames it into place last, so a stop part-way leaves
nothing behind. `make plugin-new-check` (in CI) proves a fresh plugin of each kind builds and keeps the docs in sync.

`_example` is the authoring template: the smallest MCP plugin, a C# server on the official MCP SDK in its own
container, with one tool (`get_example_fact`) and a domain descriptor. It is off unless asked for (a name with a
leading underscore is never installed by `MAF_PLUGINS` unset); CI installs it, and `make verify` asks it one routed
question while it is in use.

## A plugin folder

```
plugins/<name>/
  plugin.toml          # the manifest (required): checked against plugins/plugin.schema.json
  server.json          # an MCP plugin's server, in the official MCP Registry format
  agent-card.json      # optional bootstrap Agent Card, in the official A2A format; runtime discovery still fetches it
  compose.yml          # its own services only; never adds to api, lb or copilot-runtime
  compose.ci.yml       # its CI override (CI_MODE=1), merged after compose.yml
  lb.http.conf         # its nginx upstreams (included in http{})
  lb.server.conf       # its nginx locations, exact matches or bounded prefixes (included in server{})
  plugin.mk            # its make targets, joined to the core's (`index: index-<name>`, `verify: verify-<name>`)
  prompt.md            # a domain's prompt fragment (summary, scope, tools, examples, rules sections)
  service/             # a remote plugin's own server, built into its own image (any language)
  server/              # Maf.Lab.Plugins.<Name>.csproj: code that runs in the api (in-process plugins only)
  lib/                 # Maf.Lab.Plugins.<Name>.csproj: shared contribution code, discovered by the hosts that use it
  web/index.ts         # definePlugin({...}): code that runs in the web app (in-process plugins only)
  docs/http-api.md     # its api routes, in the table shape of docs/http-api.md, which transcludes it
  docs/guide.md        # optional feature guide, kept inside the owning folder
  docs/docs-sync.toml  # its SDK or other-host route exemptions, removed from checks with the folder
  tests/unit/          # compiled into Maf.Lab.Tests;      tests/integration/ into Maf.Lab.IntegrationTests
  files/               # what its containers mount
```

Only `plugin.toml` is required. Paths in a plugin's compose files resolve against `compose/`, the project directory,
as Compose does for every merged file: `../plugins/<name>/files/…`. A plugin's tests use the core's test support
(`ApiFactory` in C#, `@maf/testing` on the web) and leave with the folder; give the plugin's own server under
`service/` no `tests/unit` files, because those compile into the api's test host.

The automatic installed set excludes plugins whose dependencies were removed, including transitive dependants.
An explicit `MAF_PLUGINS` selection still fails before starting if a selected plugin's dependency is missing.

A `plugin.mk` may also export what a host-side run needs from the plugin's folder, the way make passes `MAF_LAB_REPO`:
billing's exports its seed paths (`Billing__SeedPath`, `Billing__AccountsSeedPath`) for `make dev`, `make test` and
the indexer. Use `export NAME ?= value`, so the environment still wins.

A host-side developer service may provide `files/dev.sh` and optional `files/dev.env`. The generic developer launcher
sources that environment and runs each installed plugin's script with its existing process cancellation handling.

A plugin that owns a corpus declares it in its manifest's `[corpus]` table: `path` (relative to its folder),
`collection` and `meta_collection` (its Qdrant collections, which no other plugin may name), `layout` (`tenants`, the
default, or `repository`) and an optional `graph` (the graph source its documents are built into). The index-admin
plugin offers a tenant admin the `tenants`-layout corpora of the installed plugins, read through the api's `/plugins`
mount. The plugin's `plugin.mk` still names the same corpus for the indexer CLI (`make index`).

Two kinds of plugin cover almost everything:

- **Remote plugins** (`mcp`, `a2a`) add a capability over a protocol the project already speaks: an MCP server or an
  A2A agent, in any language. They need no C# in the api and no React. Start here.
- **In-process plugins** (`app`) add api routes (`server/`) or screens (`web/`). Use one only for a screen or a route.

A `provider` plugin implements a port the core needs (`provides`): `decision-engine` (`IDecisionEngine`, typed
closed-set decisions with a confidence, whose contract is `docs/rules/jev-usage.md`), `chat-model` (`IChatClient`) or
`embeddings` (`IEmbeddingGenerator`). Its code is a `lib/` project that references only the abstractions and the
domain, because it is compiled into every process that asks a model, an embedder or the decision engine (the api, the
MCP servers, the indexer, the test agent, the eval); each registers only the installed providers
(`ProviderHost.AddInstalledProviders`). Exactly one decision engine must be installed, or make and every such process
refuse to start, naming the problem. `MAF_CHAT_MODEL` selects an installed `chat-model` provider; a missing or
wrong-kind name also prevents startup. Several chat providers can be installed, but only the selected one is used.
`MAF_CORE_PROVIDERS` names the core's minimum (dev: `jev ollama-cloud ollama-embeddings`), installed whatever
`MAF_PLUGINS` says. `CHAT_MODEL` / `Models:ChatModel` still select the model inside that provider. The current
embedding provider keeps query and document generators separate, applies `num_thread` on every request (dev
4/12), and refuses silent document truncation when a profile has a known context window. Only one embeddings
provider is installed; a domain with a corpus requires one. A provider changes only with a restart of the stack (`make up`), never by `plugin-on`/`plugin-off`. A provider
or model change reaches stage or prod only after the eval baselines hold with it (a manual gate). An `infra` plugin is
a store or a dev tool.

## The manifest

```toml
schema       = 1
name         = "weather"                # the folder's name; a leading underscore marks an opt-in bundled plugin
kind         = "mcp"                    # mcp | a2a | app | provider | infra
scope        = "tenant"                 # tenant (a tenant may choose it) | installation (the whole deployment)
environments = ["dev", "qa", "stage", "prod"]
description  = "Forecasts for the account's region"
depends      = []                       # other plugins it needs; installed first
# public     = true                     # listed by /api/plugins before sign-in (only a sign-in plugin needs this)
progress     = "None — every call is short"
stopping     = "None — no long work; a call stops with its MCP request"

[domain]                                # an MCP plugin: how the core uses its tools (operator-reviewed data)
id            = "weather"
order         = 90                      # where the domain sits among the others
question_key  = "in_weather"            # its routing question's key (default in_<id>)
description   = "Questions about the weather in an account's region. Not in it: billing, fees or portfolios."
tools         = ["forecast"]            # the allow-list; the endpoint comes from server.json
search_tool   = "forecast"              # called with {"query": question} on a procedural turn (forced retrieval)
guard_context = "documents"             # the content guard's context for what the tools return
prompt        = "prompt.md"             # the prompt fragment
subject       = "the weather"           # what Jev's contexts call the domain, a short noun phrase

[domain.scope_summary]                  # the domain as the out-of-scope reply names it, by language
en = "questions about the weather"
bg = "с въпроси за времето"

[domain.intent]                         # optional: the domain's clauses in the intent options, each after a generic stem
procedural = "what a named weather warning means"
data       = "a region's forecast: today's, tomorrow's or the week's"
# mixed    = "one specific …"            # how-or-why about one record; with no domain naming one, the option is left out

[agent]                                 # an app plugin that serves an AG-UI agent
name = "weather-agent"
path = "/api/weather/agent"

[topology]                              # where the topology screen probes it
service = "weather"                    # DNS discovery of replicas
health = "/health"                     # anonymous HTTP probe path
url = "http://weather:8080/health"       # optional explicit URL/port; also used outside discovery
label = "weather"
# card = "/.well-known/agent-card.json"  # optional A2A card
# id = "weather"                       # optional diagram vertex id; otherwise service or plugin name
```

A plugin with several services declares additional nodes with `[[topology.nodes]]`, using the same fields.
Only installed manifests contribute nodes. The topology report keeps the core services and asks each installed
domain server for its tools with the official MCP client, using an audience-bound token and a two-second budget.
Vector collection facts and graph connectivity use the shared store adapters. The drawing owns all edges.

A domain may also name `graph_tools`, `read_tools` and `write_tools` (a data question's routing, with their
descriptions), `card_types` (a tool's result as an AG-UI activity card), `tool_requires` (a tool offered only while
another plugin is in use) and `search_any_intent`. `plugins/plugin.schema.json` is the full reference.

`progress` and `stopping` take the forms every proposal takes (`progress-feedback`, `stop-anything`): `None — <reason>`,
`Terminal: …` and/or `Page: …`; for stopping, `Key: …; Stop: …; Recorded in: …; Shown: …`. `make docs-check` fails a
manifest without them, with an unknown key, or with a dependency that does not exist.

A remote plugin's server is described by its own standard document, never restated in the manifest: `server.json`
(MCP Registry) for an MCP server, the Agent Card for an A2A agent. Its routing questions, guard context and card types
live in the manifest, which the operator reviews, and are never read from the remote side, so a third party cannot
shape a decision or the system prompt.

A remote plugin's server is reached through the balancer (`lb.server.conf`, an exact `location`) with the caller's own
audience-bound token obtained through RFC 8693 token exchange. It validates that token itself (the platform's
`Auth` settings plus its own required audience) and
takes the tenant from its claim, never from a tool argument; `_example`'s `service/Program.cs` shows the whole of it.

`enable-plugins-per-tenant` is in progress. The core entitlement store has one allowance/enablement row per tenant
and plugin, and its access port denies a missing row. Operator changes and the audit append share one transaction;
withdrawal disables the plugin without deleting its data. Cache reads are invalidated over Redis and expire after
30 seconds even if a notification is missed. `private_to` limits a reviewed tenant-scoped plugin to its owner.
User request and turn scopes now filter domains, prompts, routing questions and plugin routes. Protocol-specific
A2A access, audience-bound remote tokens, admin pages and bootstrap are still being wired; installation
scope remains a deployment decision. See DECISIONS §88 and the active change's tasks.

## Remote audience tokens (in progress)

The API's `IPluginTokens` implementation uses OAuth RFC 8693: `TokenExchange:TokenEndpoint` is the trusted IdP
endpoint, `ClientId` identifies the confidential API requester, and `ClientSecret` comes from deployment configuration.
The subject token must name that requester in its audience. The requested audience is the installed plugin name;
permission is checked before every cached-token lookup. `Scope` defaults to `organization domain-claims`.
Exchanged tokens are signature-validated against the API's issuer keys and expire no later than the subject token.
They must carry exactly one target audience and preserve the tenant and the user's domain claims.

MCP transport and topology tools/list use the same port; API bearer passthrough is removed. The bundled resource
servers pin their own audience rather than accepting the shared API audience. Dev-login and Keycloak realm exports
are still being integrated in `enable-plugins-per-tenant`; until configured, exchange fails closed.

## Installing and removing

| Command | What it does |
|---|---|
| `make` | Installs `MAF_PLUGINS` (unset: every bundled plugin `MAF_ENV` allows, except `_example`); a plugin's routes join the balancer once its services are healthy, as with `plugin-on` |
| `make core` | The core alone: no plugin but its minimum providers (`MAF_PLUGINS=none`, `MAF_CORE_PROVIDERS`), so no domain; every turn declines before any model, decision-engine or tool call. A mode you leave: a plain `make` or `make up` brings the plugins back |
| `make plugins` | Lists every plugin: kind, scope, environments, installed, dependencies |
| `make plugin-new NAME=x KIND=mcp\|app` | Starts a new plugin (above) |
| `make plugin-on NAME=x` | Starts its services, waits until healthy, adds its routes, records the set, tells the running services |
| `make plugin-off NAME=x` | Refuses while it has open work (unless `STOP_WORK=1`, which stops the work through its store first), then the reverse |

`MAF_PLUGINS` is a comma-separated list (dependencies are added), `none`, or unset; with `CI_MODE=1` it is
`CI_PLUGINS`. `MAF_ENV` (`dev` | `qa` | `stage` | `prod`, default `dev`) names the deployment; make and the api refuse a
plugin whose `environments` does not include it, naming the plugin. `plugins/.installed` (git-ignored) is the resolved
set the api, the CopilotKit runtime and the balancer read at run time: switching a remote plugin restarts nothing.
Switching an in-process plugin restarts the api replicas one at a time, each out of the balancer's rotation first; with
one replica it needs `ALLOW_DOWNTIME=1`.

## The seams (in-process plugins)

The core never references a plugin; a plugin reaches the core only through `Maf.Lab.Plugins.Abstractions` (C#) and
`@maf/plugin-api` (web):

- `IMafPlugin` — identity (its name), nothing else;
- `IContributesServices`, `IContributesEndpoints`, `IContributesModel`, `IContributesDataMigration`, `IContributesDomainBehaviour`,
  `IContributesTurnObserver`, `IContributesOpenWork`, `IContributesDataLifecycle`, `IContributesBrandProvider`, `IContributesWriteConfirmation` —
  implement only what you give. `IContributesTurnObserver` and `IContributesOpenWork` are factory methods
  (`CreateObserver`, `CreateOpenWork`), so what they create takes its dependencies from the composed services;
- `IMafEndpoints` — your route group (behind the core's gate) and `MapPluginAgent`, the one way to serve an AG-UI agent;
  its optional thread guard can reject an unknown run before streaming. `IAgentRunInput` reads its thread from the
  official request; `AgentContents` builds Agent Framework state/step content that the core maps to protocol events.
  `IContributesDataMigration` runs an idempotent backfill of contributed tables after generic schema initialization.
- ports: `IDomainBehaviour`, `ITurnObserver`, `ITurnAccess`, `IConversationStore`, `IInstalledPlugins` (with the
  installed plugins' `Corpora()`), `IAdminJobs` (long work in the core's admin job store: started, read and stopped
  from any replica), `IBrandProvider`. A request-scoped port reads the caller from the request; none takes a principal, a tenant or a
  user.
- `IInstallationJobs` keeps repository work in the existing installation job scope; `ISystemAudit` records background
  system identifiers under the core's shared system identity. They are separate from request-scoped tenant ports.
  A plugin may reference the shared `Maf.Lab.TestGen` and `Maf.Lab.Evaluation` contracts and parsers, alongside Domain, Retrieval, A2A and Indexing.
- `IContributesDataLifecycle.CreateDataLifecycle` creates an `IDataLifecycle` over the composed services. It streams
  `DataExportRecord` values and implements idempotent deletion and tenant retention. Its explicit `DataLifecycleScope`
  comes from the authorized background job; every operation receives that job's cancellation token. Installed
  contributors remain participants when disabled for a tenant. The core participant covers conversations, message
  history, turn records, feedback, labels and dependent pending writes, including soft-deleted content. Jobs must
  check legal holds and stop writers before invoking store operations. Job orchestration, other-store contributors
  and audit pseudonymization are still separate pending `data-lifecycle` tasks; this port exposes no erasure route.
  Core user operations refuse to proceed if historical labels contain copied content whose turn has disappeared
  and whose subject cannot be recovered. Ownership must be repaired before a user export or erasure can succeed;
  the operation does not guess or silently omit those records. Tenant-wide erasure includes them. The existing
  message-retention worker now uses the same transactional content cleanup to prevent new orphan labels. Its
  retention admission also uses the durable lifecycle journal: tenant and record-type legal holds block retention
  and offboarding before any participant starts. A hold can take effect only after an already admitted destructive
  job has stopped and unwound; requesting stop alone does not release its tenant slot. The timer skips held or busy
  tenants while continuing the others. Operator hold routes, writer quiescence and full per-tenant scheduling remain
  pending; the hold store is an internal orchestration primitive.
- `IGraphBuildContribution` supplies a graph builder from a `lib/` assembly; the indexer discovers only installed
  contributors. Shared graph inputs, nodes, edges and hashes live in Domain, keeping builders independent of hosts.
- `IAppendEvalDataset` exports a labelled JSON row when a dataset exporter is installed; its Null Object keeps
  labels in the core store when none is present. Evaluation case formats, metrics, text normalization and regression
  comparisons live in the shared Evaluation library. Its dataset loader merges common/feedback files with the
  installed plugins' `evals/` files and rejects duplicate row ids.
- a tool that writes asks a person first: its first call answers MCP's `input_required` with a summary, an opaque state
  and an expiry under `WriteConfirmationKeys` (`Maf.Lab.Domain.Writes`), and your `IWriteConfirmationFlow` for that
  tool's name decides what happens next — ask the person (`AskPerson`), have the model ask them something first
  (`AskInput`), or tell the model why not (`TellModel`). The flow owns its summary's JSON Schema (each property's
  `title`, in order, is what the card shows) and the confirmed call's arguments; the core keeps the proposal, pauses the
  run, takes the answer and calls the tool with the state. The flow reaches the core only through `IWriteAudit`,
  `IConsultationScreening`, `IWriteTraceStep` and `IReviewerConsultation`; `IStatesConfirmationFacts` is for the eval.
- web: `authControls` contributes public sign-in controls to the shell; the `AdminJob` type of the job store's routes, and `definePlugin` with `routes`, `nav`, `chatPanes`, `chatSidebars` (content only: the chat draws the sidebar's
  chrome), `turnActions`, `sourceActions`, `cards`, `confirmations` (your write tool's summary on the confirmation card,
  instead of the schema's), `toolLabels`, `runObservers` and `reviewPanels`; `ChatContext`
  gives a pane the selected turn and the chat's own actions (`openPane`, `setTurnView`, `openConversation`,
  `startNew`). A plugin never navigates by itself (no `react-router`).

A plugin's routes are not served while it is not installed: a path that is the plugin's alone answers `404`, a method
it adds to a core path answers `405`. Its tables exist only while it is installed. Its routes are documented in its
own `docs/http-api.md`; `make docs` transcludes them into `docs/http-api.md`.

## Rules a plugin cannot break

Each is an architecture test (`tests/Maf.Lab.Tests/PluginArchitectureTests.cs`, `PluginContractTests.cs`, and the web's
boundary scanner in `web/src/plugins/plugins.test.tsx`), and the existing AG-UI and tenant query-path tests scan
`plugins/*/server/` as they scan `src/`:

- the core names no plugin: no file outside a plugin's folder holds its path or its project's name, and the api and
  the test hosts take plugin code only through globs;
- no reference to `Qdrant.Client`, `Neo4j.Driver` or AG-UI types: vectors and the graph only through the core's
  tenant-scoped services, AG-UI only through `MapPluginAgent`;
- no tenant parameter anywhere: the tenant comes from the principal only;
- no tool contributed in process (`AIFunctionFactory`, `AITool`): tools reach the agent only through MCP;
- a web part imports only `@maf/plugin-api`, `@maf/shared/…`, packages and its own files (tests may add `@maf/testing`);
- only the official MCP, A2A and AG-UI protocols, with nothing of the plugin's own on the wire.

## Tenant and platform administration

`/admin` (tenant-admin) hosts `tenantAdminSections`; `/platform` (platform-admin) hosts
`platformAdminSections`. Each contribution declares `{ id, label, render }` in its web module; the shell reads only
the registry in use. Saved `?section=id` links cannot open a section whose plugin is disabled. Standalone contributor
pages remain available when a shell is absent, behind the corresponding role guard. Feedback review extends the
tenant dashboard; index administration and telemetry extend the platform dashboard.

Tenant administrators enable allowed plugins. Organization-scoped platform operators grant allowances. Only
tenant-scoped plugins have checkboxes; private plugins appear only to their tenant. Checking saves immediately,
while unchecking first describes what stops and that data is kept. Esc or Cancel sends no write. Successful writes
refresh both the checkbox list and plugin registration. The platform header names the organization on every page.

`make tenant-allow TENANT=firm-a PLUGIN=billing ENABLE=1` uses an operator API token. In dev/CI, make up bootstraps
firm-a, firm-b and firm-c through the same HTTP controls after the health wait and lb reload. `shared` is never a
tenant. Repeated writes are idempotent. With dev-login absent, provide an organization-scoped `MAF_BEARER_TOKEN`
for terminal writes; TENANT cannot change the organization of an existing bearer. No browser action installs a plugin.

## Health and company identity configuration

The core's health reader works without the topology plugin. It reads `topology.url` or an explicit
`topology.service` plus relative `health`, and every declared node/replica must answer successfully.
`PluginHealth:ServicePort` defaults to the existing ASP.NET service port 8080, `ProbeTimeoutSeconds` to 2 for
the entire read (including DNS/waiting), and `CacheSeconds` to 15. An absolute URL names its own port. Bare
non-HTTP infrastructure is unknown; no port is guessed. If service discovery finds no replicas, a live fallback URL remains unknown because it cannot prove the missing replica inventory. Replica connections retain the declared HTTP authority
and TLS SNI/certificate checks, send no bearer/cookies, and follow no redirects. Caller cancellation is not cached.

For company tokens set `AUTH_AUTHORITY=https://<company-idp>/realms/<realm>` (compose maps it to
`Auth__Authority` in the API and every bundled production MCP service). `AUTH_CORE_ROLE_CLIENT_ID` defaults to
`api`; only that client's recognized core roles or recognized realm core roles are accepted. Tokens must identify
exactly one core role and one organization alias matching the existing stored tenant key (`firm-…`). Provisioner-managed
immutable IDs in the standard `groups` claim form principal GroupIds; native organization group paths remain metadata.
Legacy `tenant_id`/`role` claims never override the company organization/role mapping.

Stage/prod refuse to start without the company Authority. HTTPS is required there; loopback HTTP is accepted only
for dev/qa IdP fixtures. Tokens are checked through OIDC discovery/JWKS, pinned issuer/audience/signature/lifetime,
and only asymmetric access tokens are accepted. The dev-login plugin cannot be installed in company mode;
explicitly select the plugin set for an IdP-backed dev/qa deployment. Model-free CI clears a host company authority
and uses its development personas. Browser PKCE sign-in, real container proof
and break-glass are still tracked in adopt-company-idp, and this foundation does not claim them complete.

`AUTH_WEB_CLIENT_ID` defaults to `web` and maps to the API's `Auth__WebClientId`. With `company-login` installed,
anonymous `GET /api/identity/configuration` returns the trusted authority, public client ID, code flow/scope and
fixed callback paths; it returns 404 without a company authority. The browser must use PKCE S256.

The native external IdP templates live in `compose/keycloak`: stage/prod realm JSON, matching server build profiles,
the 26.8.0 feature catalog and a separate development-only integration fixture. The stage/prod placeholders are:
`MAF_IDP_REALM`, `MAF_WEB_ORIGIN`, `MAF_IDP_HOSTNAME`, `MAF_IDP_TLS_CERTIFICATE_FILE`,
`MAF_IDP_TLS_CERTIFICATE_KEY_FILE`, `MAF_IDP_DB_URL`, `MAF_IDP_DB_USERNAME`, `MAF_IDP_DB_PASSWORD` and
`MAF_API_CLIENT_SECRET`. Resolve them through the external deployment's secret/configuration mechanism; the
offline validator leaves them unexpanded. `MAF_WEB_ORIGIN` must be the exact web origin without a trailing slash.
No template is automatically imported or deployed by this application.

Run `make keycloak-check` with the same native feature environment overrides as the external server build, or
`python3 scripts/keycloak_check.py --features '<feature-list>' --features-disabled '<disabled-list>'` for CLI list
overrides. The gate checks realm and server profiles, rejects preview/experimental/legacy enabled features by name,
and verifies all effective scopes/mappers for PKCE and privilege/audience isolation. It does not inspect a deployed
IdP image. The external provisioner must synchronize protected, admin-only `group_ids`, `domain_roles` and
`advisor_ids` attributes; `group_ids` must contain immutable membership IDs. Native group-path mappers alone do
not provide that contract. Standard token exchange explicitly requests `organization:<validated tenant>` so a
multi-organization user's selected tenant survives exchange.

## MCP authorization metadata

Company-authenticated billing, portfolio and code hosts use the existing MCP SDK's RFC9728 metadata and challenge
handler. Bearer authentication still validates the company's JWKS-signed access token for the resource audience.
The API retains its ordinary Bearer challenge. Each MCP host requires its canonical public endpoint URL:

| Compose variable | Native host setting | Example public path |
|---|---|---|
| `BILLING_MCP_RESOURCE_URI` | `Auth__ResourceUri` on billing | `https://<public-host>/mcp` |
| `PORTFOLIO_MCP_RESOURCE_URI` | `Auth__ResourceUri` on portfolio | `https://<public-host>/portfolio/mcp` |
| `CODE_MCP_RESOURCE_URI` | `Auth__ResourceUri` on code | `https://<public-host>/code/mcp` |

The configured absolute URI must end in `/mcp` and contain no credentials, query or fragment. HTTPS is required
outside dev/qa; those fixture environments also allow loopback HTTP. Metadata inserts
`/.well-known/oauth-protected-resource` before the public resource path. It advertises the configured resource,
normalized company authority and header bearer method, with no secrets/tokens/user claims. The SDK serves it
anonymously without contacting the IdP. The SDK's 401 challenge uses that same configured absolute metadata URL,
even if a caller supplies a different Host or forwarded header. Without a company authority, these registrations
are absent and existing dev authentication remains available.

For TLS termination before an HTTP backend, configure `MCP_TRUSTED_PROXY_NETWORK` as the explicit CIDR of the
immediate ingress peer (prefer its individual IPv4 `/32` or IPv6 `/128`). Compose maps it to
`Auth__TrustedProxyNetworks__0` on all three hosts; externally managed deployments may configure further indices.
Blank/unset networks disable forwarding, including the framework's default loopback trust. Invalid CIDRs and
all-address `/0` networks are refused at startup. Only a known, non-null transport peer can forward a single
symmetric `X-Forwarded-Host`/`X-Forwarded-Proto` pair; the host must match the configured public hostname.

The immediate trusted ingress must overwrite those headers with the public host/scheme and keep metadata paths
intact. The bundled nginx adds exact per-plugin metadata locations and reserves uninstalled metadata paths as
404. Its default listener is HTTP and its default `X-Forwarded-Proto $scheme` describes that listener. If TLS
terminates before this lab balancer, the external deployment must configure the final private balancer to emit
the actual public HTTPS scheme and host, rather than overwriting them with the internal HTTP listener's scheme.
For an HTTPS-only edge this can be a deployment-owned `proxy_set_header X-Forwarded-Proto https` and
`proxy_set_header X-Forwarded-Host <public-host>` override; keep the HTTP backend reachable only through that
ingress and configure its peer CIDR in the hosts. Direct TLS ingress may instead use `$scheme` and `$http_host`.
Untrusted headers are ignored; no unrestricted forwarding or TLS deployment is enabled automatically.

## Operator session entry audit

An authenticated company operator must select exactly the token's organization with
`scope=organization:<alias>`. Operator tokens require native string `sid` and `scope` fields; missing sessions,
one-element JSON arrays, wildcard/foreign/multiple organization scopes and bare `organization` are refused.
The API records its first authenticated request for each issuer/session/operator/tenant before dispatch, including
requests later denied by a route. The event says the operator entered the organization; it does not grant or
imply content access. The tenant still comes only from the validated principal, never a path/query/header/body.

Core `OperatorSessions` receipts and `operator.enter` audit rows commit together under the existing SQLite writer
transaction, independently of tenant-admin or Compliance installation. The receipt stores a hash of the identity
tuple; neither it nor the audit event contains the raw session ID or bearer. A failed append rolls both changes
back and prevents route dispatch. Refreshed tokens with the same `sid` do not create another entry; a new login or
organization/operator/issuer does. The dev issuer remains HMAC in dev/qa and now emits an opaque `sid`; fixture
issuance may retain that session explicitly when simulating refresh. No token-hash or `jti` fallback replaces a session.

Tenant-admin's core Overview panel shows operator identities and entry times through
`GET /api/admin/operator-audit?before=<row-id>`, which remains available to TENANT_ADMIN when dashboard shells are
absent. It returns newest-first metadata only, 50 entries per page, filtered by the reader's own tenant. Ordinary
users and platform operators cannot use this tenant reader. Optional Compliance can also show the same audit kind
through its existing trail. Session/tenant changes discard the previous panel and reset pagination. Content
grants have their own lifecycle and tenant-visible list; operator entry alone gives no content grant.

## Break-glass content permission

The core records session-bound grants in SQLite and publishes their permission through the existing shared state
port, read by the API and all three production MCP hosts. Permission is never an IdP claim. Operator content access
is denied by default; explicit server metadata exempts configuration/count endpoints. Plugin installation and
entitlement gates retain their existing 404 behavior. Existing role/tenant/ownership rules still apply with a grant.
Direct MCP uses the official SDK's tool/resource/prompt request filters; initialization/tool schemas and OAuth
metadata remain available. Portfolio and code now also require the shared permission store.

Platform dashboard confirmation requires a ticket/incident reference and an integer duration of 1–60 minutes.
References contain 2–128 ASCII letters/digits or `._:/#-`, without spaces, customer messages or URL credentials/query
parameters. Grants cannot extend their original expiry, including publication retries. API issuance and its chained
audit commit together before permission becomes effective. Another session, actor or tenant cannot use the grant;
operator token exchange retains the validated native session ID. Missing, corrupt or unreachable permission state
fails closed for operators; ordinary callers keep their existing behavior.

The tenant Overview shows **Operator access to your content** with identity, reason and start/expiry/end times,
independently of Compliance. Its reader uses only the token's tenant. The platform banner remains until the grant
row reports its end, including a failed/pending revocation. Esc before confirming the dialog writes nothing; an
already issued grant ends through its own route or at the original expiry. Revocation fences late publication so
an ended grant cannot reappear. Background reconciliation completes expired/pending end metadata and audit.

The permission store requires a single standalone primary Redis with AOF enabled, `appendfsync=always` and
`no-appendfsync-on-rewrite=no`. The declared compose deployment now uses synchronous AOF writes so an acknowledged
end cannot disappear on crash/recovery. This increases disk synchronization work for shared Redis writes. The
guard checks persistence readiness/health, server identity and topology before and after each permission operation;
Lua also checks the executing server identity. Unsupported topology, a changed/restarted server, unsafe persistence
or an AOF rewrite is retriable but fails closed. Cluster/Sentinel/replica failover is not supported for this permission
plane. Runtime persistence settings must remain fixed while serving.

The connection enables the client's admin-command flag solely for read-only policy inspection. Production Redis
ACLs need `INFO`, `CONFIG GET` and the existing script/hash/key commands; configuration mutation is not used.
Only the three persistence options above are queried. Store outages prevent an end acknowledgement and keep
`endRequestedAt` visible until reconciliation finishes it. The banner follows stored `endedAt`, not a browser clock.
