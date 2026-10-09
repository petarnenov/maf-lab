## Dev issuer (dev and qa only)

| Method | Path | Body | Response |
|---|---|---|---|
| GET | `/dev/users` | — | `[{ userId, tenantId, role, domainRoles, advisorIds, label }]` predefined personas |
| POST | `/dev/token` | `{ userId, tenantId, role, domainRoles?, advisorIds?, audience?, persona? }` | `{ token, expiresAt }` |

Core roles: `TENANT_ADMIN`, `USER`, `READ_ONLY`, `PLATFORM_ADMIN`. Tenants: `firm-a`, `firm-b`, `firm-c`. A persona's domain claims
(`domain_roles` such as `billing:advisor`, and `advisor_ids`) ride in its token and are read only by the billing
server; when the request names none, the issuer adds the persona's own. For one release the issuer also accepts
`firmId` for `tenantId` and the old role names (`FIRM_ADMIN`, `ADVISOR`, `OPS`), and the api accepts a token's
`firm_id` claim for `tenant_id`.


`persona` selects a predefined user and supplies its tenant/role defaults. `operator` may choose a valid organization;
other personas keep their own tenant and role. `audience` defaults to `api`; another installed audience requires the
plugin to be in use for that tenant. Tokens preserve the persona's domain claims.
