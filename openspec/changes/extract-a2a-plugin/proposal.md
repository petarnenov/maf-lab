# Proposal

## Why

The assistant's A2A surface (its agent card, partner tokens and the billing agent handler) is core today, though a deployment may have no partner.

This is one of the follow-up extractions introduce-plugins (task 8.1) opened, in this order: `extract-billing-plugin`, `extract-portfolio-plugin`, `extract-compliance-plugin`, `extract-a2a-plugin`, `extract-coverage-plugin`, `extract-evals-plugin`, `extract-insights-plugin`, `extract-feedback-review-plugin`, `extract-index-admin-plugin`, `extract-observability-plugin`, `extract-topology-plugin`, `extract-curriculum-plugin`. It moves
code; it changes no behaviour, except where a line below says so.

## What Changes

- `plugins/a2a/` (app, tenant scope, every environment): the assistant's A2A surface — its agent card and handler
  (`AssistantAgentCard`, `AssistantAgentHandler`, renamed from `Billing*` for what they are), the partner question's A2A
  framing, the SQLite task and push-config stores and the push dispatcher, their three tables (`IContributesModel`,
  names unchanged), partner authentication (`IContributesServices`), the surface and protocol routes at the root
  (`IContributesEndpoints`), its lb snippets, the inbound half of the A2A admin page and its endpoint, its open work
  (the non-terminal tasks, so plugin-off can cancel them through the store), `verify` and `eval-a2a`.
- One port in `Maf.Lab.Plugins.Abstractions`, `IAssistantAnswer.AnswerAsync(Principal, question)`: the core answers a
  partner's question with the chat agent (screen, firm-scoped token, agent run); the plugin keeps only the A2A framing.
- `Maf.Lab.A2A` becomes a shared library like `Maf.Lab.Domain` and `Maf.Lab.Retrieval`: a plugin may reference it, and
  the core still never references a plugin. `UseA2ASpecWire` stays in the core's pipeline (the library's wire adapter).
- The billing skills (run status, the simulated start-run) stay in the a2a plugin, fenced until generalize-a2a-skills,
  guarded by billing being installed as today: a skills seam is a capability of its own, not a move.
- `compose/env/a2a.env` stays the api's (the deployment's inbound registration). The deployment's outbound A2A client
  registrations generalize from `Compliance:*` (`compose/env/compliance.env`, extract-compliance-plugin) to
  `A2A:Clients:<agent>` (client id and secret per agent consulted), mirroring `A2A:Partners:<id>` for the inbound side.
  Its endpoint defaults from the agent's manifest (`[agent]`, or its card) through the catalogue, as an MCP plugin's
  endpoint does. Client credentials are issued per authorization server, so they stay the deployment's configuration,
  not a plugin folder's.
- Also: `PartnerIdentity`'s default store keyspace (`compliance`, live Redis data) and `A2AAdminEndpoints`' agent
  fallback, left as they were by extract-compliance-plugin.
- The coverage test agent's admin section and its route move to the coverage page: `/api/admin/a2a/test-agent` becomes
  `/api/admin/coverage/test-agent`.
- `a2a-inspector` gains `depends = ["a2a", "compliance"]` (§10, 5o).
- Folder deletion deactivates dependants in the automatic installed set. An explicit selection with a missing
  dependency still fails before starting, as the installed-per-deployment requirement specifies.
- Removed: the a2a lines from the core-names-no-domain allow-list.
- Behaviour: the agent card's name reads "maf-lab assistant" (was "maf-lab billing assistant"); the partner scope
  `a2a.billing.read` is unchanged (partner compatibility; recorded as debt).
- Behaviour: the test agent's admin route moves under `/api/admin/coverage/`.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `plugins`: the catalogue gains the `a2a` plugin; deleting its folder removes it and keeps `make test` and
  `make docs-check` green.

## Principles

- SOLID: the core keeps only the seams the plugin uses (`Maf.Lab.Plugins.Abstractions`, `@maf/plugin-api`); the plugin
  depends on them, never the reverse (dependency inversion), and the core gains no branch for it (open/closed).
- Standards: the plugin contract of introduce-plugins (spec `plugins`): a manifest, the official MCP, A2A and AG-UI
  protocols, `IContributes*` (Orchard Core's module shape), compose fragments with only the plugin's own services.

## Progress

None — the change moves code; every command and page it touches shows progress as it does today, through its new folder.

## Stopping

- Key: the partner's own client
- Stop: an A2A `tasks/cancel`, as today, through the run state store
- Recorded in: the run's state in the shared store
- Shown: the task reads `canceled`

## Documentation impact

- README's A2A section; `docs/http-api.md` via the plugin's `docs/http-api.md`.
