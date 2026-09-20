# Tasks

## 1. Reading the record

- [x] 1.1 `GET /api/admin/compliance/actions?from=&to=&userId=&kind=&limit=&before=` under the `firm-admin` policy: firm from the token, newest first by row id, opaque cursor as the history endpoints do, `limit` clamped, returning the rows plus `nextCursor`; verify tests: 403 for a non-admin, another firm's rows never returned (even with a firm id parameter), paging reaches every row exactly once, filtering by person and by kind, an empty result for a period with nothing in it, and that reading adds no row and leaves the chain head unchanged

## 2. The screen

- [x] 2.1 DTOs in `web/src/api/types.ts`, route `/admin/compliance` behind `RequireAdmin`, one nav entry; verify `npm run build` type-checks and a test that a non-admin sees the same access-denied treatment as the other admin screens
- [x] 2.2 Chain panel: intact or broken **in words** (checked, predating the chain, head), and when broken the record that broke it, when, and the sentence that earlier records are unaffected — plus the standing note that the chain detects tampering rather than preventing it; verify tests for both states
- [x] 2.3 Action table: newest first with time, person, kind, action, outcome and arguments (an action without arguments says so rather than showing an empty cell), filters for person, kind and period, and "load older"; verify tests for filtering, appending older rows, and the empty state
- [x] 2.4 Export form: period, optional person, downloads the package as a file, then shows the manifest's counts, digest and chain head; verify a test that the request carries the chosen range and that the manifest is rendered after the download

## 3. Verification and docs

- [x] 3.1 In the running stack on :7171 as alice (FIRM_ADMIN): open `/admin/compliance`, confirm the chain panel matches what `verify` returns, filter the table by adam and by kind, load older rows, and export a period — check the downloaded file against the manifest shown on screen. Tamper with one row in the volume, reload, and confirm the screen names it; restore it. Confirm an ADVISOR is refused. Run `make test`, `make lint`, `COMPOSE_PROJECT_NAME=maf-lab-ci make ci-e2e`
- [x] 3.2 Update `docs/http-api.md` (the new endpoint and its cursor) and README (the compliance section points at the screen, not at `curl`); verify the sections exist
