# Tasks

The decision engine's question set narrows to the tenant's domains. That is the same request shape, so read
docs/rules/jev-usage.md, but no new question is added.

## 1. Store and gating

- [x] 1.1 Allowance and enablement rows per tenant and plugin: atomic writes, a Redis-notified cache with a 30-second
      TTL, and a snapshot per turn. Verify with a two-replica test that a change on one replica is taken by the other,
      and that a dropped notification expires within the TTL.
- [x] 1.2 `IPluginAccess.For(principal)`, read by the domain catalogue, the prompt, the decision engine's question set,
      tool selection and `/api/plugins`. Add a core filter on every plugin route group (404 when not in use). Verify the
      scenario "Two tenants, one installation".

## 2. Per-plugin audiences

- [x] 2.1 Replace bearer forwarding (`ToolSource.cs`) with a token exchange per plugin (V2 `audience`), cached per user,
      plugin and expiry. The in-repo MCP servers validate the audience. Verify the scenario "A tenant calls a disabled
      plugin's server directly".

- [x] 2.2 Keycloak setup in the realm exports:
  - the api client has "Standard token exchange" on, with client scopes `organization`, `domain-claims` and one
    audience mapper per plugin client;
  - each plugin server is a confidential OIDC client with no flow enabled (no standard flow, direct grants or
    service accounts), whose id is the audience;
  - the web client's tokens carry the api in `aud`.

  Verify with a Keycloak 26.8 Testcontainer the scenario "The exchanged token carries the tenant and the domain
  claims", that an exchange requested by any other client is refused, and that a subject token without the api in
  `aud` is refused.
- [ ] 2.3 `dev-login` mints a token for a persona and an audience (`audience` in `/dev/token`'s JSON body, and
      `make dev-token PERSONA= [TENANT=] AUDIENCE=`, whose output api-route terminal targets export as
      `MAF_BEARER_TOKEN`).
      `EvalAgentHost` mints one dev token per tenant and plugin audience the same way (no exchange, no Keycloak in CI).
      `mcp-inspector`'s `start.mjs` requests one token per listed server's audience on refresh. Verify that
      `make eval SUITE=selection` runs against audience-checking servers, for firm-a and firm-c.

## 3. Dashboards

- [ ] 3.1 `tenant-admin` and `platform-admin` shells with their section registries and the `PlatformAdmin` policy:
      plugin checkboxes, confirmation on unchecking and on withdrawing an allowance, and private plugins (`private_to`).
      Add `make tenant-allow TENANT= PLUGIN= [ENABLE=1]` through an operator token for the tenant's organization.
      Verify the dashboard and checkbox scenarios.
- [ ] 3.2 Dev and CI bootstrap:
  - `dev-login` gains an `operator` persona (`PLATFORM_ADMIN`, plus the requested tenant): `make dev-token
    PERSONA=operator TENANT= AUDIENCE=api`;
  - `make up`, `ci-e2e` and `evals.yml`, after the health wait and the lb reload, allow and enable every installed
    tenant-scoped plugin for firm-a, firm-b and firm-c (`shared` has no allowance row), idempotently, with one line
    per tenant and Ctrl+C between calls exiting 130. If the loop ever takes longer than 3 s in a terminal, it shows a determinate
    done/total bar instead (progress-feedback).

  Verify that a fresh `make` and `make eval SUITE=selection` (firm-a, firm-c) behave as before this change.

## Working evidence (2026-10-09)

Task 1.1 foundations added: core entitlement rows, atomic permission/audit transaction, Redis notification,
monotonic 30-second TTL, immutable access snapshots, and in-flight read invalidation protection. Per-turn integration was completed and verified in the next step; task 1.2 retains the remaining request/protocol work.

Owner instruction: no evals or indexes for this goal. Do not execute the listed live eval commands or CI commands
that index. Continue implementation and verify non-eval behavior with protocol/store/UI fixtures.

Foundation evidence: warnings-as-errors build and web build clean; 48 targeted .NET tests (tenant store/cache,
audit regression, principal parsing and architecture) passed; real Redis two-connection replica fixture passed.
These are non-eval tests. The complete request/turn, audience and dashboard requirements remain open.

Task 1.1 completed: real Redis replica invalidation and fake-clock dropped-notification expiry were proved in the
foundation tests; the chat test now changes the allowance during a held search and proves the old turn completes
with its snapshot while the next turn sees no enabled domain. Request, turn and tool scopes use the same access
snapshot. Task 1.2 remains open while protocol-specific A2A access and remaining readers are completed.

Request/turn evidence: 53 targeted tests passed (tenant gating/cache, ordinary chat, restart persistence,
no-domain behavior, plugin hosting, audit chain and core source guard). Two tenants expose different domains,
plugin/card registration and route access; a disabled domain contributes no decision question or tool; prompt
fragments are isolated; changing an allowance during a held turn applies only to the next turn. The fixture
bootstrap is idempotent across restarts. Warnings-as-errors build and docs-check (131 Python tests) are clean.

Task 2.1 work: RFC 8693 token exchange and audience-bound MCP/topology clients implemented; signatures, audience,
identity, claim preservation, permission-before-cache and expiry checks added. In-repo MCP audiences are pinned.
Runtime dev-login, direct hosted protocol scenarios and the Keycloak integration remain; task stays open.

Audience evidence (2026-10-09): 56 targeted tests passed across RFC 8693 request/response validation, permission-
before-cache, cache expiry and separation, concurrent exchanges, cancellation, single-audience HTTP rejection,
actual official MCP transport headers, topology probes, tenant gating and architecture. Build has zero warnings;
docs-check (131 Python tests) and all 63 strict OpenSpec items pass. No index or eval command ran. Task 2.1 remains
open until runtime dev-login and Keycloak integration and the remaining hosted protocol scenarios are complete.

Task 2.3 work: optional dev-login issuer/selector, operator organization token, audience request field, dev-token
command, per-server Inspector tokens and remote audience-token harness wiring added. Persona/domain-claim tests
and targeted UI tests pass; the complete task is not checked off yet. Evaluator code was edited/compiled only, not run.

Dev-login evidence (2026-10-09): 41 targeted .NET tests (issuer/operator/audience, persona claims, CLI HTTP request,
permission gating, resource-server audiences and plugin contract) and 21 targeted web tests passed. Temporarily
removing its folder kept the build, 16 non-eval route/gating tests, web build and docs-check green; folder restored.
Inspector syntax checked; evaluator source is compiled only. Task 2.3 remains open for full protocol fixture coverage;
the owner's banned live-eval commands were not executed.

Administration and protocol work (2026-10-09): optional tenant/platform dashboard modules and section registries,
organization-only audited permission controls, private/installation scope handling, confirmation-before-off, usage
counts and paged permission audit implemented. Index administration and telemetry moved behind PlatformAdmin.
Operator dev organization selection issues a new API credential; all pages show the acting organization. Lazy
registration now waits before redirecting saved plugin URLs and removes old-session contributions immediately.

`make tenant-allow` and dev/CI bootstrap use the same operator HTTP control route after health/lb reload. Fifteen
terminal fixtures prove idempotency, private scope, no shared rows, determinate slow progress, token redaction, safe
SIGINT/SIGTERM between atomic calls, and interruption followed by transport failure returning 130.

Protocol fixes cover partner snapshots, dynamic cards, persisted task/push isolation by partner and enabled tenant,
selected owner/audit attribution, continuation owner binding and deferred SSE scope restoration. Redirects are
disabled for the exchange transport. Independent review findings were repaired; the strengthened ownership regressions
passed, and the final independent review found no blocker, major or minor defect. Real company-IdP organization selection, realm exports/Keycloak container evidence, the full
inspector/harness coverage and owner-prohibited live eval/bootstrap validation remain open. No eval or index ran.

Current verification: warnings-as-errors solution build, web lint/build, docs.py check (61 routes) and all 63 strict
OpenSpec items passed. The proper MTP protocol/admin/exchange suite passed 112 tests before the final regression
strengthening; separate tenant store/gating/transport/permission selection passed 40. Focused registration/UI
suite passed 33. Escape-cancellation regression was mutation-tested: removing Escape handling made it fail, and
the original implementation was restored. All 728 full-web assertions passed, but the run has an existing
unhandled OTLP exporter network error to localhost:3000. Full make docs-check reaches an existing Python HTTP
fixture blocked by sandbox TCP bind permissions (145 tests: 144 passed); direct docs synchronization is clean.
These full gates are not reported as passing. Task checkboxes stay open until their complete stated requirements
and dependent validations are proved.

Task 1.2 completion audit: the domain catalogue, prompt fragments, decision question sets, tool selection and
`/api/plugins` all use the authenticated request/turn snapshot; plugin endpoints apply the core gate, including
protocol partner adaptation. Two-tenant domain/card/route/question/tool/prompt fixtures and frozen-turn tests pass.
Authenticated A2A protocol fixtures prove disabled domains never reach a tool/model, persisted tasks/push configs
cannot cross partner/tenant boundaries, deferred SSE retains its scope, and withdrawal applies to the next request.
Final code review is clean. Task 1.2 is checked; per-plugin token exchange's real IdP validation and tasks 2.2/2.3/3.1/3.2
retain their broader/dependent requirements and remain open.

Final strengthened/legacy fixture selection: 98 tests passed (A2A tenant/handler, actual MCP protocol, portfolio and
code resource-host fixtures). Both unit and integration test projects build clean; integration execution and all
index/eval commands remain excluded. The stored-owner test starts without a tenant name, and firm-b attribution
is asserted with both tenants enabled, so these assertions isolate ownership instead of merely testing history or
first-enabled fallback. Four review iterations completed; final reviewer found no code defect or regression gap.

Next code work: task 2.2 requires adopt-company-idp's JWKS/organization principal foundation, which is not yet in
AuthOptions/DevJwt authentication (currently symmetric development validation). Task 3.1 remains open for the real
organization-scoped operator flow/directory and verification of remote plugin health: the existing core health
reader reports in-process contributors as ok and reads only topology.url, while bundled service manifests mostly
declare topology.service/health. Do not treat that metadata as evidence that a remote MCP/A2A service is healthy.
Fix/verify through the existing resolver/probe seam rather than inventing host ports or depending on the optional
topology plugin. Runtime inspector/harness fixtures and final live validation are also still outstanding.

Further code work (2026-10-09): remote health now shares the Domain/Hosting DNS seam independently of optional
topology. The core probes all declared nodes and discovered replicas under one total two-second budget, keeps
HTTP authority/Host/TLS SNI with per-IP connection routing, prevents pooled first-IP reuse, uses a 15-second
monotonic descriptor/settings cache, and never caches caller cancellation. Undeclared remote protocols/bare
non-HTTP infrastructure and undiscovered live fallback replica inventory report unknown. Malformed DNS input is
isolated instead of faulting every plugin listing. Tests cover non-cooperative DNS and cancellation behind a busy
probe gate. Actual TCP/TLS handshake is not claimed verified in the restricted environment.

The IdP dependency has progressed from symmetric-only validation to the company JWKS/organization/group
foundation in adopt-company-idp. API and actual resource host fixtures now exercise that handler; RFC8693 maps
company organization/core-role/groups and validates the subject before any exchange or cached return. Private
operator directory, PKCE organization selection, real Keycloak configuration/container evidence, operator-content
grants, inspector/harness fixtures and final live validation are still open. Remote health's code gap is closed;
that does not complete all of task 3.1. All original open task scopes remain intact.

Final current-state verification (2026-10-09): warnings-as-errors solution build (including a full analyzer pass)
passed with zero warnings/errors; the final identity/health/issuer/tenant selection passed 131 tests and the A2A,
actual dev MCP, topology and administration regression selection passed 57. Three deployment-composition tests,
web lint/build, direct docs synchronization (61 routes), strict OpenSpec validation (63 items) and git diff --check
passed. The backend unit and HTTP/MCP integration cells are covered; this slice makes no frontend implementation
change (PKCE remains open). Independent review has zero blocker/major findings; the existing exchange-entry cache
minor remains explicitly recorded. Actual TCP/TLS and real Keycloak container/realm tests are unverified; the
earlier full-web exporter/network and full docs-check TCP-bind limitations are not overwritten as passed.
No index, eval or graph command ran.

Task 2.2 completion audit (2026-10-09): the stage/prod exports configure only api for supported standard V2
exchange, with default basic/roles/domain-claims/plugin-audiences and optional organization. Each deployed
resource (billing, portfolio, compliance) is a confidential OIDC client with standard/direct/service-account
flows disabled, and has its own audience mapper. The web audience is api only. Code is dev/qa-only by its
manifest and is represented in the isolated fixture rather than stage/prod. Seventeen realm contract tests
and make keycloak-check pass, including missing/duplicate subject mapper regressions.

The pinned Keycloak 26.8.0 Testcontainer executed four native cases successfully (not just compilation): real
PKCE subject token with api audience, production PluginTokenExchange selecting organization firm-a for an A+B
member and retaining tenant/domain/group claims with exact billing audience, another requester refused for
absent requester audience, a web-without-api subject refused by api exchange, and configured issuer mismatch
refused despite genuine discovery/signing keys. Runtime-discovered missing sub was repaired in all three realm
exports with the native subject mapper. Independent code/security reviews report no findings. This completes
2.2; browser OIDC, inspector/evaluator and prohibited live-eval requirements remain on their own open tasks.

Tasks 2.1/2.2 final verification: the native exchange scenario now targets billing specifically and additionally
uses the actual billing MCP host with native discovery/JWKS. A seeded firm-a billing run succeeds, a firm-b run
is refused, and the original api-audience token receives HTTP 401. The disabled-plugin direct-access regression
now covers both billing and portfolio with exact HTTP 401, failed token minting for the disabled tenant, and a
working enabled firm-c control. All four real Keycloak tests and 105 focused exchange/identity/audience/dev/MCP
cases pass. Both test projects build with warnings-as-errors, strict OpenSpec validation passes all 63 items,
and git diff --check is clean. New independent code/security reviews report zero findings. Existing exchange
cache-entry retention is the already-recorded minor, not a new finding. Frontend was not changed in this slice.
Task 2.1 is now complete as well. No index, eval, or graph command ran; tasks 2.3/3.1/3.2 retain their full scope.

Task 2.3 Inspector/harness follow-up (2026-10-09): the real Inspector launcher now has nine dependency-free
Node tests (node --experimental-vm-modules --test plugins/mcp-inspector/tests/start.test.mjs), with filesystem,
HTTP, timers and child-process boundaries controlled. They verify each installed MCP server's audience/persona,
URLs, hourly renewal, installed-list changes, refusal removing old credentials, malformed replies, atomic writes,
overlapping refreshes, write-failure recovery and signals. Refreshes are serialized and both interval callbacks
handle errors. Independent review caught a stalled request blocking that queue; a 10-second AbortSignal timeout
now covers both fetch and body consumption, with deterministic recovery tests for both phases and a changed
installed set. Removing token validation in an isolated temporary copy made the malformed-token regression fail
with Bearer undefined, confirming the test detects the protected behavior.

Twenty-one DevAudienceTokens cases cover exact tenant/persona/audience requests without subject forwarding or
exchange, authenticated plugin snapshots, refusal/malformed responses, cancellation, and actual dev-login API
issuance for firm-a/firm-c. Empty/non-string credentials and malformed/partial plugin snapshots are rejected.
The combined provider/dev-login/actual-MCP regression gate passes 32 cases; warnings-as-errors build passes.
Final independent code/security reviews have zero findings; Node syntax and git diff --check pass. No frontend
UI changed, no EvalAgentHost was started and no model, index, eval or graph command ran. The terminal bearer
variable belongs to API-route tools (design specifies data-lifecycle), not the direct MCP harness/Inspector.
Task 2.3 remains unchecked because its explicit live make eval SUITE=selection gate is still owner-prohibited;
the isolated credential/protocol tests are not presented as an executed selection evaluation.
