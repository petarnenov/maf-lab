# Tasks

Jev: task 4.3 changes how the routing question sets are built (from the domain catalogue). Read
docs/rules/jev-usage.md before it. The sets stay closed, fixed per deployment and asked in
one request.

Depends on `rename-firm-to-tenant`, which lands first. Followed by `introduce-provider-plugins`, `adopt-company-idp`,
`enable-plugins-per-tenant`, `document-acls`, `data-lifecycle` and `add-document-parsing`.

## 1. Pin today's behaviour

- [x] 1.1 Add tests that pin today's domain routing, forced search and card mapping for billing, portfolio and codebase
      (`ChatTurnRunner`, `DataToolRouter`, `CodeToolRouter`, `DataCards`, `Domains.OfTool`). Verify they pass on `main`
      unchanged.
- [x] 1.2 Record `make eval SUITE=selection` on `main` as the reference for task 4.

## 2. Infrastructure seams (no behaviour change)

- [ ] 2.1 nginx:
  - a core-only `nginx.conf` with `include conf.d/http/*.conf` and `conf.d/server/*.conf`;
  - `return 404` reservations for the plugin route shapes (`/<name>/mcp`, the A2A agent paths).

  Verify that the lb loads with `conf.d` holding only the generated `00-api.conf`, and that `/code/mcp` answers 404, not the SPA, with no snippet.
- [x] 2.2 Plugin settings from manifests:
  - a `PluginCatalogue` reads `plugins/.installed`, the manifests and `server.json` on a Redis `plugins-changed`
    message and every 30 seconds (no file watcher). A malformed manifest keeps the last good set and is reported;
  - `Agent:Servers` becomes a map keyed by plugin, still binding the indexed form for one change;
  - topology services and the runtime's agents come from manifests.

  Verify with binding tests and the runtime's conformance check.
- [x] 2.3 Makefile:
  - `MAF_PLUGINS` and `MAF_ENV`, with dependency expansion and cycle detection;
  - `plugins/.installed` written by rename;
  - `COMPOSE_FILE` from the installed `plugins/*/compose.yml`, each holding only its own services;
  - `-include plugins/*/plugin.mk`;
  - `compose/lb/conf.d/` git-ignored and regenerated from `.installed` by every `make up`; both parts written, then
    `nginx -t && nginx -s reload`;
  - `make core|plugins|plugin-on|plugin-off`. On: services → healthy → snippet → reload → `.installed` → publish. Off:
    list the plugin's open work (`IContributesOpenWork`); refuse unless `STOP_WORK=1`, which cancels through the store
    and waits for terminal states; then the reverse order;
  - the api upstream moved into a tracked, name-based template `compose/lb/api.upstream.conf`, which every `make up`
    copies to `conf.d/http/00-api.conf` (a fresh checkout gets the default); `scripts/docs.py` reads the template when
    it resolves upstreams, so `make docs-check` passes with no stack and an empty `conf.d`;
  - the api restart for in-process plugins, one replica at a time: a transient IP-list `00-api.conf` without the
    replica → reload → `docker restart -t 40` (`ShutdownTimeout` 30 s, `stop_grace_period` 40 s) → healthy →
    next. At the end and in the trap, restore the name-based form and reload. With one replica, refuse unless
    `ALLOW_DOWNTIME=1`;
  - `RunState` gains `Instance`, written by its owner (`ChatRunFilter`, `RunStateTracker.Snapshot`). Run-owner
    heartbeats go in Redis under a per-process id (`Name-Guid`), every 5 s with a TTL of 20 s, kept until
    `ApplicationStopped`. On read, a run whose owner has no
    heartbeat is marked cancelled. Both that write and the owner's terminal write are conditional (a Lua compare
    that also keeps the RunGrace expiry), so neither overwrites a terminal state;
  - progress and Ctrl+C as the proposal says.

  Verify that `docker compose config` leaves the core services byte-identical for `none`, `all` and each single
  plugin, and that a plugin-on of a remote plugin recreates none of api, lb or copilot-runtime. Verify the restart
  scenarios: a streaming answer is not cut by a closed keepalive connection, and a killed replica's run is marked cancelled
  on its next read, and that a `make up` after an interrupted restart serves `/api` (the upstream is name-based again).
- [x] 2.4 Make `index_if_empty.sh`, `verify_lb.sh` and `conformance.mjs` run a plugin's part only when `/api/plugins`
      lists it.
- [x] 2.5 Move the inspector ports into the dev-only compose override. `MAF_LAB_REPO` stays required while the api
      itself mounts the repository (`docker-compose.yml:290,315`, for coverage and index admin). It becomes
      plugin-only when those move to their plugins, in their follow-ups.

## 3. Contracts

- [x] 3.1 `Maf.Lab.Plugins.Abstractions`:
  - `IMafPlugin` (identity) and the `IContributes*` interfaces;
  - `IMafEndpoints`, with `MapPluginAgent` implemented in `Agent/AGUI/`;
  - `IDomainDescriptor`, `IDomainBehaviour`, `ITurnObserver`, `IConversationStore` and the `IBrandProvider` port
    (implemented by `add-white-labeling`);
  - `AddMafPlugins()` discovery;
  - `Directory.Build.targets` referencing `plugins/*/server/*.csproj`.
- [x] 3.2 `GET /api/plugins`: the plugins in use, each one's health, domain ids and card ids, and any invalid manifest;
      since task 4.11, signed in, also the domains in use (`domains: [{ id, scope }]`).
      Anonymous before sign-in, with only `public = true` plugins (only `dev-login`, so empty in stage and prod).
      Documented in `docs/http-api.md`.
- [x] 3.3 Architecture tests:
  - core → plugin references;
  - plugin → Qdrant, Neo4j or AG-UI types;
  - tenant parameters;
  - in-process tools (`AIFunctionFactory`, `AITool`).

  Widen the roots of the existing `AGUIProtocolOnlyTests` and tenant query-path tests from `src/` to `src/` plus
  `plugins/*/server/`. Verify each test fails once on a planted violation inside a plugin folder (then reverted).
- [x] 3.4 `ApiFactory` takes the installed set. Add boot tests with no plugin and with all plugins.
- [ ] 3.5 Web:
  - `web/src/plugins/api.ts` (`definePlugin`, registries);
  - `web/src/shared/` (moved `formatDate`, `Page.module.css`, `StopHint`, `useEscToStop`, `ErrorBoundary`);
  - a loader over `import.meta.glob` + `/api/plugins` (anonymous, then signed in);
  - a per-plugin error boundary;
  - the ESLint boundary rule;
  - the web image context at the repository root, with a `.dockerignore`.

  Vitest: the app with no plugins (nav = Chat, no aside) and with all.
- [x] 3.6 `docs.py`:
  - validate manifests against `plugins/plugin.schema.json` (keys, scope, environments, `progress`, `stopping`,
    `depends`);
  - glob `plugin.mk`, `lb.http.conf` and `lb.server.conf`;
  - widen `ROUTE_SOURCES`;
  - generate the README `plugins` block;
  - add a `[layout]` line for `plugins/`.
- [x] 3.7 Plugin contract suite: one shared xUnit/Vitest suite run against every plugin folder. It checks that:
  - the manifest is valid;
  - the stack boots with and without the plugin;
  - `progress` and `stopping` are present;
  - its routes are absent when it is not installed;
  - its folder is deletable, with its tests going with it.

  Verify it runs for each plugin present and fails on a planted broken manifest.
- [x] 3.8 Environments: make and the api refuse a plugin not allowed in `MAF_ENV`. Verify both refusals name the
      plugin.
- [x] 3.9 CI builds `full` and `product` image variants. Check that `product` holds no dev-or-qa-only plugin code, and
      have qa run `product`.

## 4. Domains as data

- [x] 4.1 Build the domain catalogue (`DomainCatalogue`, design §6) from the installed plugins' descriptors. `Domains` reads it.
- [x] 4.2 `ChatTurnRunner`: the codebase and portfolio branches become descriptor and behaviour lookups. Portfolio's
      focus and summary logic goes behind `IDomainBehaviour`.
- [x] 4.3 The routers build their closed sets from the catalogue. The guard and the answer check read `GuardContext`.
      Jev review (jev-usage §7) of `DataToolRouter`, `CodeToolRouter` and `JevIntentClassifier.DomainQuestions` as built
      from the catalogue: the questions, ids, option sets and descriptions are the ones `main` sent, byte for byte
      (`DomainRoutingPinTests` pins every set for billing, portfolio and codebase); each is still a closed, atomic Noul or
      Choice with `none`/`other` where the list may be incomplete; every routing, domain and code-need question still
      rides in the one intent request over the same state; arguments are taken by code (`IDomainBehaviour.BindRead`,
      fixed patterns), never by Jev; a disabled domain is simply not an option; thresholds, pinned model and logging are
      unchanged. Non-English inputs: the Bulgarian and Latin-script Bulgarian run-id cases in `DataToolRoutingTests`
      and the selection eval's Bulgarian cases.
- [x] 4.4 Offer fee adjustment only when `compliance` is in use.
- [x] 4.6 Remove every billing-first assumption:
  - `AgentOptions.McpEndpoint` as the first server;
  - the billing fallback in `Domains.OfTool`;
  - the hard failure only billing has;
  - `Domains.Billing` in core files.

  Billing becomes a descriptor like the others, still served by `mcp-retrieval` until its follow-up.
  - `BuiltInDomains.*` and domain names outside `BuiltIn/` only in the allow-listed, annotated files; the architecture
    test (`CoreNamesNoDomainTests`) enforces the list.

  Behaviour removed, pinned by:
  - `DomainRoutingPinTests`: an unknown tool → no domain (null);
  - `CodebaseDomainTests`: an unreachable billing server → billing `Unavailable`, the turn's other tools offered; every
    needed server down → the call fails; a configured server of a domain not in use → never contacted;
  - `PluginHostTests`: no implied billing server;
  - `PortfolioDomainTests`: `InDomain` = the most probable domain's probability;
  - `AgentMcpIntegrationTests`: `propose_fee_adjustment` offered only with a compliance reviewer (4.4).
- [x] 4.7 Split `Prompts/system.v5.md` into a generic core prompt plus the billing, portfolio and codebase fragments,
      assembled from the domains in use. Verify `make eval SUITE=selection` and the answer-quality suite are no worse
      than 1.2.
- [x] 4.11 No domain in use: the turn declines before any model or Jev call, and the chat page says so up
      front. Verify with a test that the scripted model and the fake engine receive no request.
- [x] 4.5 Verify: the tests from 1.1 are unchanged and green, and `make eval SUITE=selection` is no worse than 1.2.
      "Unchanged" means unchanged except the 4.6 removals listed there; "unchanged" protects the pinned behaviour: the
      tests' domain constants moved home (`Domains.X` →
      `BuiltInDomains.X`), and the setups that set `AgentOptions.McpEndpoint` configure `Servers["billing"]` instead,
      with every assertion untouched.

## 5. Proof extractions

- [x] 5.1 `a2a-inspector`, `mcp-inspector`, `redis-insight` and `neo4j-browser` (infra, dev/qa): one plugin each, with
      its service and nav link. The mcp-inspector's server list comes from the installed manifests. Verify that
      `make core` runs none of them, that each `make plugin-on NAME=…` brings back only its own, and that
      `MAF_ENV=stage` refuses each.
- [ ] 5.2 `code` (mcp + app). Moves:
  - the mcp-code service and its lb snippet;
  - the code index and graph targets;
  - its `[domain]` table;
  - the code snippets pane and `/api/code/snippets`, with `sourceActions` for code sources;
  - its tests (`CodeToolRoutingTests`, `CodebaseSearch*` and the others) into `plugins/code/tests/`;
  - the `search_codebase` label and any code card or label from the core web into the plugin's web part;
  - its line in plugins/mcp-inspector/files/start.mjs (the code plugin's server.json lists it instead);
  - `code` added to `CI_PLUGINS`.
  - Known gap: `make lint-web` runs `eslint .` inside `web/`, so plugin web files are not linted; the import boundary
    for them is the Vitest scanner in `web/src/plugins/plugins.test.tsx` (test-only imports included).

  The codebase domain's pin assertions (1.1: `DomainRoutingPinTests`, the code-request part of `DataToolRoutingTests`,
  the codebase fragment of `SystemPromptTests`) moved verbatim into the plugin, run in the three-domain view; the core
  pins keep billing and portfolio. This is the allowed move, as in 4.5.

  Verify the scenarios "A remote plugin is switched off" and "A plugin is deleted".
- [ ] 5.3 `monitor` (app):
  - split `TurnTrace` into the core record and `ITurnObserver`, and store reasoning on the core turn;
  - move into the plugin, with its own table: `LiveTrace`, the full model capture, the prompt dump, `TraceRetrieval`,
    `RunFrameRecorder`, the trace routes and retention;
  - web: the monitor gets its own store and run observer, `chatReducer` loses `traces`, and time travel goes through
    `turnView`;
  - its tests move with it.

  Verify that core-only turns make no Redis trace write and no trace request, and that with the monitor on every
  existing monitor test passes.

  Done in two commits: C3a (server: the core record, the observer host, the monitor's server part, table, routes and
  retention, their tests, the docs and deltas) and C3b (web: the monitor's web part, run observers, the turn-view
  override, the review panel slot). Both are in; the box stays open for the live checks (a core-only turn
  making no Redis trace write and no trace request, on the running stack).

- [ ] 5.4 `conversation-history` (app, installation scope, installed by default):
  - add `IConversationStore` to the abstractions (task 3.1), implemented by the core: page and search, rename, and a
    soft delete that records `conversation.delete` in the audit;
  - move `HistorySidebar*`, with its drawer, collapse, toggle and rail, into a `chatSidebars` entry; the core shows the
    toggle only when the registry is non-empty;
  - move the list, rename and delete routes over `IConversationStore`;
  - move the `['conversations']` invalidation (`ChatPage.tsx:181`) to the plugin's `runObservers.onRunEnd`;
  - keep the header's existing "New conversation" (`ChatPage.tsx:305-307`) as core;
  - move or rewrite the core tests that use the moved routes:
    - `ComplianceApiTests`, `MessageRetentionTests`, `HandleTests`, `ChatPage.stop.test.tsx`;
    - `ChatHistoryTests`: its list, search, paging, rename and delete parts move; its title, open and continue parts
      stay core;
    - `ChatPage.history.test.tsx`: the sidebar part moves; "New conversation while a stored one is open" stays core
      on the header button;
  - drop the sidebar's own "New conversation" and add a core web test for the header button.

  Verify the scenario "Without the conversation list", the chat-history MODIFIED scenarios with and without the
  plugin, and that deleting the plugin folder keeps `make test` green.

## 6. Writing your own

- [ ] 6.1 `plugins/_example/`: a small MCP server with a domain descriptor, off by default. A CI leg installs it and
      asks one routed question against the stub.
- [ ] 6.2 `make plugin-new NAME= KIND=mcp|app` from templates. Verify that a scaffolded plugin builds, starts, and passes
      `make docs-check`.
- [ ] 6.3 Write `docs/plugins.md`, and make the CLAUDE.md, project.md, README and DECISIONS §81 updates from the
      proposal.

## 7. CI

- [ ] 7.1 A core-only e2e leg next to the all-plugins leg: `make core`, then `verify`, then one chat turn on the stub.
      It asserts the decline and that no model, engine or tool call is made.

## 8. Follow-up changes (one plugin each, code moves only)

- [ ] 8.1 Open proposals in this order:
  1. `billing` (with `qdrant` and `neo4j`); makes Jev's text domain-generic (design part B, decision 6) and re-measures
     the guard, intent and answer-check suites; removes its line from plugins/mcp-inspector/files/start.mjs; deletes the `Agent__Servers__billing__*` compose lines, which would
     otherwise shadow its manifest's server
  2. `portfolio`; removes its line from plugins/mcp-inspector/files/start.mjs
  3. `compliance`; removes its `BuiltInDomains.LegacyCapabilities` entry
  4. `a2a`; removes its line from the core-names-no-domain allow-list
  5. `coverage`
  6. `evals`; hosts the code server through the catalogue's endpoint instead of `BuildApp`, which frees
     `src/Maf.Lab.CodeSearch`, the graph-tool tests (GraphBuildAndTool, GraphTraceEvent, GraphIntegration) and the code
     graph builder to move under the code plugin's folder
  7. `insights`; removes its line from the core-names-no-domain allow-list
  8. `feedback-review`; removes its line from the core-names-no-domain allow-list
  9. `index-admin`
  10. `observability`
  11. `topology`; removes its line from the core-names-no-domain allow-list
  12. `curriculum`
