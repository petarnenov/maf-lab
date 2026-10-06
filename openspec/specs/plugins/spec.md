# plugins Specification

## Purpose
Everything optional in maf-lab is a plugin: one folder under `plugins/` with a manifest, its own services, routes, make
targets and web part. The core is a domain-agnostic shell that reaches a plugin only through its seams and the official
protocols, so a deployment installs, switches and removes plugins without changing the core.

## Requirements

### Requirement: A plugin is one folder with a manifest
Every optional capability, every domain included, SHALL live in one folder `plugins/<name>/`. The folder holds a
`plugin.toml` manifest stating:
- its name;
- its kind (`mcp`, `a2a`, `app`, `provider` or `infra`); a `provider` also names what it `provides`, and the change
  that introduces each provided capability defines how many of it may be installed;
- its scope (`tenant` or `installation`);
- the environments it may be installed in;
- a description;
- the plugins it depends on;
- how its work shows progress;
- how it is stopped.

The folder MAY hold compose services, lb snippets, make targets, an api module, a web module, a domain prompt fragment,
specs, docs and tests. Removing the folder SHALL remove the plugin from every part of the system, and no tracked file
outside the folder SHALL name it.

#### Scenario: A plugin is deleted
- **WHEN** `plugins/code/` is deleted and `make` is run
- **THEN** the stack builds and starts, `make test` and `make docs-check` pass, and nothing serves or shows the code tool

#### Scenario: A manifest without stopping
- **WHEN** a `plugin.toml` has no `stopping` entry, or one not in the accepted form
- **THEN** `make docs-check` fails and names the plugin and the missing entry

### Requirement: The core holds no domain
The core SHALL be a domain-agnostic assistant shell: chat agent, conversation memory, sign-in, answer feedback, typed-decision routing, the
guard, the answer check (typed decisions), write confirmation and the plugin system. Every domain, billing included, SHALL be a plugin. No
core code SHALL name a domain, treat one domain as first or default, or attribute an unknown tool to a domain.

The system prompt SHALL be assembled from a generic core prompt and the prompt fragments of the domains in use, each
fragment kept in its plugin folder.

#### Scenario: Billing is not installed
- **WHEN** `portfolio` is installed and `billing` is not, and a portfolio question is asked
- **THEN** the turn is answered from the portfolio domain, its prompt has no billing wording, and no billing tool is
  offered

### Requirement: With no domain in use the assistant declines
When no domain plugin is in use, a chat turn SHALL end before any model or decision-engine call with a fixed message:
no domain is enabled, and the administrator can enable one. The chat page SHALL say the same before the
first message.

#### Scenario: Core only
- **WHEN** `make core` is run and a user sends a message
- **THEN** the fixed message is shown in the chat's design, and no model call, decision-engine call or tool call is made

### Requirement: Environments decide what may be installed
Each manifest SHALL list the environments (`dev`, `qa`, `stage`, `prod`) the plugin may be installed in. make and the
api SHALL refuse to start with a plugin not allowed in `MAF_ENV`, and SHALL name it.

CI SHALL build two image variants from one commit:
- `full`, for dev and qa;
- `product`, which contains no code of a plugin not allowed in stage or prod.

stage and prod SHALL run the `product` images qa tested.

#### Scenario: The monitor in prod
- **WHEN** `MAF_ENV=prod` and `MAF_PLUGINS` includes `monitor`
- **THEN** make fails before starting anything, and the api refuses to start, naming `monitor`

#### Scenario: The product image is clean
- **WHEN** the `product` variant is built
- **THEN** a CI check finds no assembly and no web chunk of a dev-or-qa-only plugin in it

### Requirement: Core never depends on a plugin
No core project or core web module SHALL reference or import a plugin. A plugin SHALL reach the core only through the
published seams:
- `IMafPlugin` (identity) and the `IContributes*` interfaces it implements;
- `IMafEndpoints`, including `MapPluginAgent` for an AG-UI agent;
- `IDomainDescriptor`;
- `IDomainBehaviour`;
- `ITurnObserver`;
- `IConversationStore`;
- `IBrandProvider`, contributed through `IContributesBrandProvider` (used by `add-white-labeling`);
- the web plugin API in `web/src/plugins/api.ts` and `web/src/shared/`.

#### Scenario: The core imports a plugin
- **WHEN** a file under `web/src` outside `plugins/` imports from `/plugins/`, or a core assembly references a
  `Maf.Lab.Plugins.*` assembly
- **THEN** lint or the architecture test fails and names the import

### Requirement: Tools reach the agent only through MCP
Every tool offered to the chat agent SHALL come from an MCP server listed for an enabled domain. No plugin SHALL
register an `AIFunction` or `AITool` with the agent, and no seam for that SHALL exist.

#### Scenario: A plugin registers a tool in process
- **WHEN** a plugin assembly calls `AIFunctionFactory` or implements `AITool`
- **THEN** the architecture test fails and names the plugin

### Requirement: Remote plugins speak only the official protocols
A plugin not in this repository SHALL be a remote plugin: an MCP server, an A2A agent, or an AG-UI agent. It SHALL use
only the official MCP, A2A and AG-UI protocols, with no custom methods, headers, events or `_meta` keys the core depends
on.

How the core uses a remote plugin SHALL come from its operator-reviewed manifest, never from the remote side. That
covers its domain, routing questions, guard context, prompt fragment and card types.

#### Scenario: The example plugin
- **WHEN** `_example` is installed and a question its routing question accepts is asked
- **THEN** the turn is routed to the example domain, its search tool is called over MCP, and the answer cites its
  result

### Requirement: Plugins keep the project's rules
A plugin assembly SHALL NOT reference `Qdrant.Client`, `Neo4j.Driver` or AG-UI types. It SHALL read vectors and the
graph only through the core's tenant-scoped services, and expose an AG-UI agent only through `MapPluginAgent`.

No plugin tool, endpoint or query SHALL take a tenant parameter. A plugin's routing questions SHALL come from its
manifest, never from a request, a tool argument or the model.

#### Scenario: A plugin builds its own Qdrant query
- **WHEN** a plugin's server project references `Qdrant.Client`
- **THEN** the architecture test fails and names the plugin

### Requirement: Someone can write their own plugin
`make plugin-new NAME=<name> KIND=mcp|app` SHALL scaffold a plugin that:
- builds;
- starts;
- passes `make docs-check`;
- is listed by `make plugins`.

`docs/plugins.md` SHALL describe the manifest, both kinds, scopes and environments, the seams and the rules.

#### Scenario: A new plugin from the template
- **WHEN** `make plugin-new NAME=weather KIND=mcp` is run and then `make plugin-on NAME=weather`
- **THEN** its service starts healthy behind the lb and `make plugins` lists it as installed and healthy

### Requirement: Installed per deployment, read at run time
`MAF_PLUGINS` SHALL decide which plugins are installed:
- unset, every bundled plugin allowed in `MAF_ENV` except `_example`;
- `none`, no plugin (other than the core providers, once `introduce-provider-plugins` defines them);
- otherwise a comma-separated list.

The set SHALL be expanded with dependencies, and make SHALL fail on a cycle or a missing plugin before starting
anything. The api, copilot-runtime and the lb SHALL read the installed set and the manifests at run time. Installing
or removing a plugin without a `server/` part SHALL NOT recreate or restart them.

#### Scenario: A remote plugin is switched off
- **WHEN** `make plugin-off NAME=code` is run on a running stack with a chat run streaming
- **THEN** the run finishes (each api replica drains before it restarts, since the plugin has a `server/` part), the code
  services stop, `/code/mcp` answers 404, its tools are not offered from the next turn, lb and copilot-runtime were
  not recreated, and the api replicas restarted one at a time

#### Scenario: A dependency is missing
- **WHEN** `MAF_PLUGINS` names a plugin whose dependency does not exist
- **THEN** make fails before starting anything and names the missing plugin

### Requirement: The web shows only plugins in use
The web SHALL read `GET /api/plugins` and register only the plugins in use. Registration covers routes, nav links,
chat panes and sidebars, turn actions, source actions, tool labels, monitor tabs, cards, run observers and review
panels. Before sign-in the route SHALL answer only with
plugins whose manifest declares `public = true`, which is none in stage and prod. Each plugin SHALL render inside its own error boundary. A plugin that is in use but
whose service is unhealthy SHALL be shown as unavailable, not hidden.

#### Scenario: A plugin fails to render
- **WHEN** a plugin's pane throws while rendering
- **THEN** that pane shows its error and the chat keeps working

### Requirement: Removing a plugin stops its work first
Before a plugin is removed, its open work SHALL be listed. Removal SHALL be refused while work is open, unless the
operator asks to stop it. Stopping SHALL go through the store that owns each item, which marks it cancelled
atomically, and SHALL wait until every item is terminal before the plugin's services stop.

#### Scenario: Coverage run in progress
- **WHEN** `make plugin-off NAME=coverage` runs while a coverage run is in progress
- **THEN** it lists the run and refuses; with `STOP_WORK=1` it cancels the run through its store, waits until the run
  is cancelled, and only then stops the coverage services

### Requirement: Restarting the api for an in-process plugin keeps runs
Switching an in-process plugin SHALL restart the api replicas one at a time. Each replica SHALL first be taken out of
the balancer's rotation by a reload, then drain its runs within the api's shutdown timeout, which Docker's stop grace exceeds. Runs left
at the end SHALL be recorded cancelled. The replica SHALL then become healthy and be added back by a reload before the
next replica starts. The balancer's api upstream SHALL live in a generated file whose permanent form is name-based, rewritten
by every start. Any address list SHALL exist only during the restart, and SHALL be removed at its end or on
interruption. With a single replica, the restart SHALL require explicit consent to downtime. A run read while its owning replica has no heartbeat SHALL
be marked cancelled, so no run stays running forever.

#### Scenario: A chat answer streams during the restart
- **WHEN** an in-process plugin is installed while a chat answer streams from replica 1
- **THEN** the answer completes, no request is cut by a closed keepalive connection, new requests go to replica 2
  while replica 1 restarts, and both replicas serve after the last reload

#### Scenario: A single replica
- **WHEN** `API_REPLICAS=1` and an in-process plugin is installed without `ALLOW_DOWNTIME=1`
- **THEN** make refuses, saying the restart would interrupt the api, and changes nothing

#### Scenario: A draining replica's run is not taken for orphaned
- **WHEN** a page polls a run that its replica is still draining
- **THEN** the run stays running, because the replica keeps its heartbeat until it has stopped

#### Scenario: A replica dies mid-run
- **WHEN** a replica is killed while it owns a running chat run
- **THEN** the next read of that run marks it cancelled, and the page that started it shows the stopped outcome

### Requirement: The conversation list is a plugin, memory is core
The core SHALL keep conversation memory: creating a conversation, reading one by id with its pending confirmations,
"New conversation" in the chat's header, and the history the agent reads on every turn. The list of past
conversations, with its search, rename and delete, SHALL be the installation-scoped `conversation-history` plugin,
installed by default. It SHALL reach storage only through the core's `IConversationStore`. Without it, the chat SHALL
still start and continue conversations with full memory, and SHALL show no sidebar chrome.

#### Scenario: Without the conversation list
- **WHEN** `conversation-history` is not installed and a user returns to a conversation by its URL
- **THEN** the conversation opens with its messages, the agent answers with the earlier turns in context, no sidebar,
  drawer, toggle or rail is shown, "New conversation" is in the chat header, and `GET /api/conversations` is not
  served (`405 Method Not Allowed`, with `Allow` naming the core's methods: the path's `POST` stays the core's)
