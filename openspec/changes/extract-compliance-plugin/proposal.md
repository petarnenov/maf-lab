# Proposal

## Why

The compliance reviewer, its client and the audit screen are still core, so a deployment without a reviewer still carries them, and fee adjustment's `tool_requires` still resolves through configuration.

This is one of the follow-up extractions introduce-plugins (task 8.1) opened, in this order: `extract-billing-plugin`, `extract-portfolio-plugin`, `extract-compliance-plugin`, `extract-a2a-plugin`, `extract-coverage-plugin`, `extract-evals-plugin`, `extract-insights-plugin`, `extract-feedback-review-plugin`, `extract-index-admin-plugin`, `extract-observability-plugin`, `extract-topology-plugin`, `extract-curriculum-plugin`. It moves
code; it changes no behaviour, except where a line below says so.

## What Changes

- **`plugins/compliance/`** (kind `a2a`, tenant scope, every environment):
  - `service/`: the reviewer agent (the project `Maf.Lab.ComplianceAgent`, moved from `src/`), in its own container.
    `compose.yml` runs it (two replicas, `COMPLIANCE_REPLICAS` from its `plugin.mk`) and holds only that service.
    `lb.http.conf`/`lb.server.conf` route `/compliance` to it.
  - The api's client settings stay in the core's `compose/env/compliance.env`, given to the api whether the plugin is in
    use or not. They are the deployment's outbound client registration: OAuth 2.0 client credentials are issued per
    authorization server, so per reviewer, and a fragment never changes the api's environment (introduce-plugins §2).
    So `make plugin-on/off NAME=compliance` needs only the replica restart. extract-a2a-plugin generalizes it to
    `A2A:Clients:<agent>`. Rejected: an api override in the plugin's compose (the rule, and a restart keeps the old
    environment), a mounted file (secrets in a tracked folder, no interpolation), exporting from `plugin.mk` (compose
    still needs the key on the api).
  - `server/` (`Maf.Lab.Plugins.Compliance`):
    - the client, `ComplianceConsultant`, implements extract-billing's `IReviewerConsultation`;
    - the audit screen's routes stay at their paths (`/api/admin/compliance/{verify,actions,export}`).
    - `IContributesServices` registers the client once for every host that composes the installed plugins: the api,
      and the eval (`PluginHost.InstalledServices`).
  - `web/`: the audit screen (`/admin/compliance`, a route for administrators, and its nav link).
  - `tests/`: the agent's, the client's and the screen's tests. `docs/http-api.md`: its routes, transcluded.
- **The audit records stay core (5y)**: `ToolAudit`, `AuditKinds`, `AuditChain`, and the export's own record. The
  screen reads them through a new port in `Maf.Lab.Plugins.Abstractions`,
  `IAuditTrail { VerifyAsync; PageAsync; ExportAsync }`, implemented by the core (`CoreAuditTrail`). The tenant comes
  from the request's principal and the port returns DTOs. The plugin holds no `DbContext`.
- **A consultation is a step of a write's audit**: the client records through `IWriteAudit`, with kind
  `a2a.consultation`, the operation as the step, and identifiers as before. That port gains an additive overload with the
  step's duration. **Behaviour:** the record's actor is the request's principal instead of a synthetic
  `maf-lab-assistant`, and its conversation and turn are filled in.
- **`tool_requires: compliance`** resolves to "the compliance plugin is in use" (the catalogue), not to
  `Compliance:BaseUrl`.
  - Without the plugin, the core's `NoReviewer` (a Null Object) answers the port with "unreachable", so a flow still
    resolves. The tool that requires a reviewer isn't offered anyway.
  - The policy name a plugin's routes require is shared through `Maf.Lab.Domain.Tenancy.PolicyNames`.
- **Builds**:
  - `Directory.Build.targets` references `plugins/*/service/*.csproj` from the test projects only (never the api or the
    product image), under the extern alias `service`: a service's top-level `Program` is global and would shadow core
    types. Only the compliance tests that host the agent opt in (`extern alias service;`).
  - The agent leaves `maf-lab.sln` and the coverage module list.
- **Tests split by responsibility**:
  - billing's fee-flow rules run on a core-owned `ScriptedReviewer` at the port;
  - how each hostile verdict is read off the wire is compliance's (`HostileVerdictTests`);
  - what billing's flow then writes is billing's (`InjectionA2ATests`, given the result each verdict is read as).
  - The billing and real-reviewer path end to end over A2A is covered by `make ci-e2e` (`verify_lb.sh` 4.6 and the
    live fee flow).
- **Make**:
  - `--scale compliance` and `COMPLIANCE_REPLICAS` leave the core Makefile (the plugin's compose reads the replicas);
  - the eval's `Compliance__*` come from the plugin's `plugin.mk` through a general `EVAL_ENV`;
  - `a2a-inspector` declares `depends = ["compliance"]`.
- Removed: `BuiltInDomains.LegacyCapabilities`. `BuiltIn/` (two name constants) and the scanner's exemption stay: `BuiltIn/`
  is deleted by the extraction that moves its last reader. The readers are JevStatistics (insights), FeedbackEndpoints
  (feedback-review), TopologyProbe (topology), and EvalAgentHost and DomainSuite (evals).
- `TopologyProbe` reads the raw `Compliance:BaseUrl` until extract-topology. `PartnerIdentity`'s `compliance` store
  keyspace (live Redis data) and `A2AAdminEndpoints`' fallback stay as they are until extract-a2a.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `plugins`: the catalogue gains the `compliance` plugin; deleting its folder removes it and keeps `make test` and
  `make docs-check` green.

## Principles

- SOLID:
  - dependency inversion: the core keeps only the seams the plugin uses (`IReviewerConsultation`, `IAuditTrail`,
    `IWriteAudit`, `@maf/plugin-api`), the plugin depends on them, and never the reverse;
  - open/closed: the core gains no branch for it.
- Standards:
  - the plugin contract of introduce-plugins (spec `plugins`): a manifest, the official A2A protocol, `IContributes*`
    (Orchard Core's module shape), and compose fragments with only the plugin's own services;
  - Ports and Adapters, with a Null Object for an absent reviewer and a test double at the port;
  - C#'s `extern alias` for colliding top-level programs.

## Progress

- Page: the audit screen shows its loading state while a page of the record loads, as today. A turn waiting for a review
  says a review is under way and roughly how long one takes, as today.

## Stopping

- Key: Esc on the audit screen while it waits; Esc on the chat page while a review runs
- Stop: the screen's request is aborted; the turn stops through CopilotKit's stop, and the consultation sends the
  reviewer A2A `tasks/cancel` within 5 s, as today
- Recorded in: the review task's state in the reviewer's shared store
- Shown: the screen says it stopped; the turn says it was stopped

## Documentation impact

- README's layout block (generated); `docs/http-api.md` (the routes, via the plugin's `docs/http-api.md`).
- DECISIONS §81 part H:
  - `tool_requires` by catalogue;
  - the service/ glob under extern alias `service`, amending part F's rejection. Rejected: a csproj-name glob,
    namespacing the template's `Program`, qualifying call sites;
  - the consultation audit's actor;
  - `IAuditTrail`;
  - the api's outbound client registration staying core;
  - the client's own wire records, pinned by a test;
  - why an outbound DTO's tenant field passes the scanner;
  - `BuiltIn/` deleted by the extraction that moves its last reader.
