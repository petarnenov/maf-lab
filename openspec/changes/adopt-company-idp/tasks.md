# Tasks

No decision-engine call is added or changed, so the Jev review checklist does not apply.

## 1. Sign-in

- [ ] 1.1 OIDC sign-in in the web (authorization code + PKCE). The api and the in-repo MCP servers validate the IdP's
      JWKS-signed tokens. The principal comes from the organization, roles and standard claims, and gains
      `GroupIds`. The web client's tokens carry the api client in `aud` (an audience mapper). Verify with a Keycloak
      26.8 Testcontainer and a fixture realm that a user of organization A gets principal {tenant A, role, groups}, and
      that a token from another issuer is refused.
- [x] 1.2 `dev-login` plugin (installation scope, dev and qa only): today's symmetric dev issuer, unchanged in kind. No
      Keycloak service in compose or CI. Verify that `MAF_ENV=stage` refuses it.

## 2. Operator and break-glass

- [x] 2.1 `PLATFORM_ADMIN` and `platform_operator`. An operator acts on a tenant only through
      `scope=organization:<alias>`, and no route takes a tenant parameter. Verify that a request without the role is
      refused. The api mirrors an operator's first request per session into the tenant-visible audit; verify that the
      tenant's dashboard lists it.
- [x] 2.2 Break-glass grant in our store (reason, at most one hour, audited, listed for the tenant). Content routes
      refuse an operator without an active grant. Verify the scenarios "Reading a conversation without a grant" and
      "The tenant sees the access".

## 3. Supported features only

- [x] 3.1 A check on the stage and prod realm export that fails on any preview or experimental feature flag, naming it.
      Verify that it fails on a planted `token-exchange-delegation`.

## 4. Documents

- [x] 4.1 CLAUDE.md, DECISIONS §89 (Keycloak pin, supported-features rule, break-glass), http-api.md and the README.

## Working evidence (2026-10-09)

Task 1.1 foundation implemented: API and billing/portfolio/code use the shared standard JWT bearer handler with
OIDC discovery/JWKS, asymmetric algorithm/issuer/audience/lifetime checks, and access-token type validation. The
company mapper requires native JSON organization/realm/client-role objects, exactly one stored-namespace
organization alias and one recognized core role. Provisioner-managed immutable IDs in the standard groups claim
form GroupIds; native organization group paths are metadata and grant no ACL membership. Legacy tenant/role fields
cannot override the company mapping. Malformed, ambiguous, wrong-
issuer/signature/audience, expired, ID and delegation tokens are rejected.

Stage/prod require Auth:Authority and cannot fall back to HMAC. Environment/authority/core-role-client settings
reach the API and all three production MCP compose services, and make dev maps the same public settings. CI clears
an inherited company authority to keep its dev-login fixture. Installing dev-login with company mode is refused.
No actual company credential was read or written. Development issuer behavior stays available in dev/qa.

The actual API and official MCP host fixtures use RSA-signed tokens and discovery/JWKS backchannel fixtures,
without sockets/index/bootstrap collection work. The API permission-route fixture verifies role refusal and that
a spoofed legacy firm-b claim cannot redirect a company organization-A permission write. Token exchange has 30
company RSA/JWKS cases covering subject validation before outbound/cache, native organization/role/group mapping,
multiple subject audiences, exact target audience, domain claims, altered identities/groups and retry after refusal.
Independent security review found no blocker/major after the deployment propagation fixes. The pre-existing
exchange fingerprint/gate cache is an explicit minor follow-up (D89), retained under the owner's scope rule.

Full task 1.1 stays open: browser authorization-code/PKCE sign-in and real Keycloak 26.8 Testcontainer proof
remain to implement/verify. Native realm templates and SDK protected-resource metadata are now present, as
described below. Operator entry and break-glass completion are recorded below. The already
extracted dev-login plugin is not counted as complete identity migration while dependent validations are missing.
No eval/index/graph refresh or index-running stack command ran. D89 records the actual new decision; planned §83
references were corrected because existing D83 belongs to the earlier CLI/A2A stopping decision.

Completion audit for task 1.2: the installation-scoped dev-login manifest allows dev and qa only; its personas,
issuer/selector/commands are optional contributions, and the core keeps no identity-issuing route. Hosted tests
assert the specific disallowed-plugin startup reason for both stage and prod (rather than passing on any startup
error), and a separate configuration regression refuses dev-login in company mode without registering its
token provider. No Keycloak service/image occurs in the compose/CI service definitions. Removing the plugin
folder was already tested in the preceding migration evidence; the shared bearer validation does not issue
identity tokens to a caller without that plugin's endpoint. This component task is complete; full identity
(1.1) remains open; completion of the operator, break-glass, export-gate and documentation components is recorded below.

Final current-state verification (2026-10-09): warnings-as-errors solution build (including a full analyzer pass)
passed with zero warnings/errors; the final identity/health/issuer/tenant selection passed 131 tests and the A2A,
actual dev MCP, topology and administration regression selection passed 57. Three deployment-composition tests,
web lint/build, direct docs synchronization (61 routes), strict OpenSpec validation (63 items) and git diff --check
passed. The backend unit and HTTP/MCP integration cells are covered; this slice makes no frontend implementation
change (PKCE remains open). Independent review has zero blocker/major findings; the existing exchange-entry cache
minor remains explicitly recorded. Actual TCP/TLS and real Keycloak container/realm tests are unverified; the
earlier full-web exporter/network and full docs-check TCP-bind limitations are not overwritten as passed.
No index, eval or graph command ran.

Native company configuration and feature-gate evidence (2026-10-09): `compose/keycloak` contains the pinned 26.8.0
native stage/prod RealmRepresentation templates, separate native keycloak.conf server build profiles, an explicit
feature catalog derived from the pinned Profile.java, and an isolated development integration fixture. Server
feature flags are not invented realm JSON fields. `make keycloak-check`, `make ci` and the CI specs job validate
both production realm/profile pairs offline without importing or contacting the IdP. The validator resolves native
list/individual/environment/CLI overrides, exact supported versions and feature dependencies. A planted native
`token-exchange-delegation` exits 1 and names that feature. Preview/experimental/legacy versions, unknown versions,
missing required organization/standard-exchange features, and invalid native dependency combinations are refused.

The realm contract checks public code/PKCE S256 with exact redirects, API-only web audience, resource-only exchange
audiences, all effective scope/client-local mappers and role mappings, non-composite core roles, and protected
admin-only privilege attributes. Extra Device/CIBA/JWT grants and non-api exchange requesters are refused,
including native case-insensitive boolean variants. Independent review found and closed extra effective role-scope
and grant-attribute bypasses. The signal fixture releases FIFO EOF after delivery to avoid an artificial POSIX
open/read race. Thirty-one Python regressions pass; deleting the supported-status guard in an isolated in-memory
mutation makes its regression fail. The production gate itself passes.

The company-login installation plugin exposes six public sign-in configuration fields anonymously; configured
WebClientId reaches the API from `AUTH_WEB_CLIENT_ID`. Group claims use protected provisioner-managed immutable IDs,
not names/paths; the external provisioner must maintain membership values. Production exchange selects the caller's
validated `organization:<alias>` before cache/HTTP and rejects wildcard/foreign-organization scope. The native
fixture exercises identity-first code/PKCE login, a multi-organization member, production plugin token exchange,
and requester-audience refusal. All three fixture cases are collected by the native xUnit runner, but execution
remains unverified because Docker is unavailable. The browser SDK is unavailable locally and npm DNS fails, so
frontend PKCE is still open. RFC9728 metadata now uses the existing MCP SDK with canonical resource URLs and
explicit trusted HTTPS ingress handling, as described below. No frontend layer was changed in this slice.

Current verification: full warnings-as-errors/analyzer solution build passed with zero warnings/errors; 85 selected
company login/identity/authentication/exchange/dev-surface backend tests and three composition tests passed.
Direct documentation sync covers 62 routes, strict cached OpenSpec validation passes all 63 items, and git diff
--check passes. These do not stand in for real native Keycloak execution or the previously unverified full-web
exporter/network and full docs-check TCP-bind gates. No eval, index or graph command ran.

MCP authorization metadata evidence (2026-10-09): billing, portfolio and code now register the installed MCP SDK's
RFC9728 authentication handler. Their default authentication remains Bearer; only their challenge uses the MCP
scheme. Metadata advertises the configuration-pinned public resource URI, normalized authority and header bearer
method (plus the SDK's empty scopes_supported field), anonymously and without an IdP discovery/JWKS request.
The challenge uses a configured absolute metadata URI regardless of caller Host/forwarded headers. API and dev
without company Authority keep their existing Bearer behavior. Each company resource requires Auth:ResourceUri;
the three compose variables and exact well-known balancer routes are documented. Missing/unsafe URIs fail startup.

HTTPS forwarding is explicit: only a present transport peer in configured Auth:TrustedProxyNetworks may forward
one symmetric host/proto pair, constrained to the public hostname. Empty networks accept no default loopback
trust; invalid CIDRs and /0 networks fail startup. The native SDK compares hostname and scheme, not the request
port; a regression verifies that a different request port cannot change the advertised resource/challenge port.
Per-plugin metadata locations preserve the full path, and uninstalled metadata paths answer 404. External TLS
deployment must configure the final trusted private ingress to retain the public HTTPS scheme/host; the default
lab balancer's HTTP listener and $scheme alone cannot establish that public TLS boundary.

Forty-two new backend cases cover 15 startup/configuration unit cases and 27 real-host HTTP metadata/challenge/
forwarding cases, including all three resource hosts, IPv4-mapped peers, missing/untrusted peers, spoofing, limits,
API/dev behavior and empty scopes metadata. Final selected identity/configuration/exchange/dev/metadata tests pass
127 cases; 37 Python identity-composition and Keycloak cases pass. Full warnings-as-errors/analyzer build passed
with zero warnings/errors, docs synchronization and strict cached OpenSpec validation pass. The real-host
metadata regressions fail on all three hosts when the configured resource pin is temporarily deleted; the original
source was restored exactly and rebuilt. Independent code/security reviews finish with zero blocker/major and
no remaining findings in this slice. No frontend layer was changed; PKCE still needs the unavailable browser SDK.
Native Keycloak, nginx and actual TCP/TLS execution remain unverified (Docker daemon unavailable; no local nginx).
Task 1.1 stays open; operator entry and break-glass completion are recorded below. No additional task checkbox
is inferred from the metadata component alone.
No index, eval or graph operation ran.

Operator entry completion evidence (2026-10-09, task 2.1): validated `platform_operator` still normalizes to
PLATFORM_ADMIN, and every shared company JWKS resource boundary now requires native string sid/scope fields with
exactly organization:<validated alias> for that role. Bare/wildcard/foreign/multiple organization scopes, missing
sessions, and one-element JSON arrays that the JWT handler otherwise flattens are refused. Ordinary company
roles retain their existing claim requirements. No route takes a tenant selector. DevJwt remains HMAC/dev-qa-only
and adds an opaque sid with an optional explicit session value for refresh fixtures.

The API records the first authenticated operator request before permissions/authorization, including requests
later denied by a route. A core OperatorSessions receipt keyed by SHA256 of [configured issuer,sid,actor,tenant]
commits in the same serializable SQLite writer transaction as the existing chained operator.enter append.
Refresh/restart/concurrent replicas reuse the receipt; changing any identity component records another entry.
Append failure rolls back receipt and audit and prevents route dispatch. The receipt contains no raw sid, and the
audit includes only actor, tenant, UTC time and entry kind, with no request URL/query/body/bearer/session contents.
The additive initializer installs the table in an existing database without losing audit history or needing an
optional plugin.

GET /api/admin/operator-audit exposes only id/at/operatorId and an exclusive before cursor, newest-first 50 per
page, filtered by the validated tenant and TENANT_ADMIN policy. The tenant Overview renders those entries through
the actual tenant-admin module even when Compliance is absent. Its panel resets and hides previous entries on
tenant/token changes, including late old responses and an old pagination cursor. Optional Compliance reads the
same core audit kind through its existing trail. An entry is neither a content read nor a break-glass grant; 2.2
completion is recorded below.

Verification: 60 new OperatorSessionAudit cases and four additional shared-audience scope cases are collected;
the final selected backend gate passes 235 tests across operator/identity/metadata/permissions/audit/tenant/dev
regressions. The full analyzer warnings-as-errors build passes with zero warnings/errors. Twenty-five focused UI
tests plus five import-boundary checks pass. Actual-module tests live under the tenant-admin plugin, using test-only
App/PluginsProvider exports; no core-to-plugin import or boundary exemption was introduced. Web lint/build pass.
The full web suite now passes all 744 tests across 104 files with exit 0: the earlier localhost:3000 OTLP error was
an exporter flush escaping a fetch-only test stub. The browser tracing test now mocks only the collector/exporter
boundary, retaining the real SDK provider/processor/instrumentation and existing header/lifecycle assertions.
Production telemetry configuration and tracing code are unchanged; independent review found no issue in this fix.
Thirty-seven Python composition/Keycloak cases and the production feature gate pass. Documentation synchronization
covers 63 routes, strict cached OpenSpec validation and git diff --check pass. Independent code/security reviews
have zero blocker/major and no remaining findings. Native Keycloak/TLS execution and browser PKCE remain unverified
under task 1.1, and the separate full docs-check socket-bind limitation is not claimed passed. No eval, index or
graph operation ran.

Break-glass completion evidence (task 2.2, 2026-10-09): core SQLite ContentAccessGrants metadata records the
validated operator/session/issuer/tenant, bounded reference, original start and expiry, end request and acknowledged
end. Issuance plus its chained audit commit before shared permission publication. Grant reason/start/expiry never
update, no unended session grant can be renewed, and duration is 1–60 minutes. Reason is an identifier/reference,
not tenant free text. The audit includes actor/tenant/reference/start/expiry and requested/actual end timestamps,
without raw session/token/request content. The additive core table and endpoints require no dashboard/Compliance.

The shared own-store permission is keyed to the validated session tuple and has its original absolute expiry.
Revocation first persists an end request, then atomically fences/deletes the matching shared permission, then commits
the durable endedAt/audit acknowledgement. Tombstones prevent delayed activation from resurrecting an ended grant
and an old revoke cannot delete a newer grant. Reconciliation finishes expiration/pending ends; publication retries
consume the original lifetime. API requires both live unended/nonending SQL metadata and matching shared state.
Direct MCP hosts check the same shared session permission using official tool/resource/prompt request filters.
Native and dev exchange retain the operator sid. Missing/expired/revoked/corrupt/unreachable state denies content;
IdP grant bits, request headers/body/tenant selectors grant nothing. Existing ownership and role policies remain.

All authenticated operator API routes default to requiring content permission; configuration/count exceptions are
server metadata, and installation/entitlement filters keep their precedence. History/pending/chat/traces/snippets,
optional source/generated content and document drift are protected, while configuration/discovery/schema routes
retain their existing behavior. The UI confirms a bounded reference/duration, Esc before confirmation writes
nothing, no renewal control exists, and the global active/ending banner remains until stored end acknowledgement.
Tenant Overview lists identity/reference/start/expiry/end, independently of Compliance; session/token changes
discard previous data and late responses. Grant metadata APIs are actor/session/tenant-bound and TENANT_ADMIN
listing exposes only the token's tenant, with fixed cursor pages and no raw session/permission keys.

Independent review found one major durability gap: every-second Redis AOF could restore a revoked permission after
a crash despite SQL endedAt. It was fixed by requiring verified standalone Redis synchronous AOF fsync before
replies. Compose now uses appendfsync=always. Fresh policy/readiness/topology/run_id checks surround activate/read/
revoke and Lua verifies the actual executing run_id. Unsafe policy, rewrites, identity ambiguity, replica/cluster/
Sentinel topology fail closed. Persistence configuration remains fixed while serving; INFO and read-only CONFIG GET
inspect only three non-secret options, and mutation is not used. This synchronous shared-store I/O tradeoff is D89.

Actual verification: warnings-as-errors build passes; final selected backend gate passes 340 cases including grant,
shared permission, actual API/MCP enforcement, operator entry, identity/exchange, permission/tenant/issuer and dev
regressions. Native Redis Testcontainers execute all 12 cases successfully, including actual Lua expiry/fencing,
unsafe persistence refusal and SIGKILL/AOF restart recovery. The TTL check compares the exact PEXPIRETIME deadline
instead of assuming container/host clocks are identical. Native tests are no longer described as unverified.
The frontend's full gate passes 787 assertions in 107 files, lint/build pass, and the new focused grant UI/real-module
tests cover confirmation/Esc/paging/end acknowledgement/outage/session races. Thirty-nine Python composition and
Keycloak cases pass. Direct docs sync covers 67 routes, strict cached OpenSpec validation and git diff --check pass.
Independent final code/security review has zero blocker/major and no remaining findings. CLAUDE.md, D89, HTTP API,
README and plugin deployment docs record the pin/features/grant/durability/stop behavior (task 4.1). Full docs-check
socket-bind and real TLS/native Keycloak/browser PKCE gates remain separate, unclaimed here. No index/eval/graph ran.

### Native Keycloak runtime follow-up (2026-10-09)

The pinned Keycloak 26.8.0 Testcontainer now executes successfully: all four native cases pass, covering real
authorization code/PKCE login, organization A plus stable group IDs and core role, the production V2 exchange
with exact resource audience/domain claims, requester/subject-audience refusals, and a real signed token rejected
when its issuer differs from the configured authority even though discovery advertises its signing key.

Runtime verification exposed two gaps. The test HTTP client did not apply the browser's Secure-cookie loopback
exception; a fixture-only adapter now preserves CookieContainer path/expiry handling and restricts every request
to the exact HTTP loopback origin, without following redirects or changing production transport. The realm exports
also omitted the native subject mapper, so genuine access tokens lacked sub. Stage/prod/fixture now attach the
reviewed basic scope with oidc-sub-mapper to web/api (and the fixture's alternate requester clients). Realm validation
requires a unique effective subject mapper, with mutation regressions for missing/disabled/duplicate mappings.
Native source: https://github.com/keycloak/keycloak/blob/26.8.0/services/src/main/java/org/keycloak/protocol/oidc/mappers/SubMapper.java.

The integration project builds with warnings treated as errors. Browser OIDC remains incomplete: the SDK install
probe still fails with ENOTFOUND for registry.npmjs.org. Task 1.1 is therefore still unchecked.

Independent code and security reviews of this runtime repair both report zero findings. Seventeen realm contract
tests and make keycloak-check pass. A broader Python discovery run passed 188 of 189 cases; the unrelated
test_ollama_stub failed to bind its HTTP test server under the sandbox, so that broad gate is not reported green.

The later per-plugin completion audit strengthened the native exchange scenario from the synthetic weather
resource to billing itself: exact billing audience/domain claims, shared principal mapping, actual billing MCP
own-tenant success and foreign-tenant denial, and original api-token HTTP 401 all pass in the four-case native run.
This completes the corresponding enable-plugins-per-tenant 2.1/2.2 work, while browser OIDC task 1.1 stays open.
