# Proposal

## Why

The compliance record is now trustworthy and exportable, and completely out of reach of the person who needs it.
Checking whether the chain is intact, or producing a package for an authorised request, means minting a token and
running `curl` piped into `jq` — which is not how a compliance officer works, and is exactly the manual step the
export endpoint was built to remove.

The record is also invisible day to day. "What did this person do last week" is now indexed and one query away, but
there is nowhere to ask it, so the index added for it is used by nothing.

## What Changes

- An **`/admin/compliance` screen** for FIRM_ADMIN, beside the two admin screens that already exist: the chain's
  state in plain words, a browsable log of actions, and an export that downloads as a file.
- The chain state shows **intact or broken in a way nobody can misread** — and when it is broken, which record broke
  it and when, since everything recorded before that point is still evidence.
- A **browsable action log**: the firm's actions newest first, filterable by person, kind and period, paged. This
  needs a new read endpoint; the two existing endpoints answer "is it intact" and "give me everything", neither of
  which can be paged through on screen.
- The **export form** takes the period and optionally a person, downloads the package as a file, and then shows its
  manifest on screen — the counts, the digest and the chain head — so they can be quoted in the covering email
  without opening the file.

## Capabilities

### Modified Capabilities

- `compliance-audit`: a new requirement for browsing the record — what can be filtered, how it is paged, and that
  reading it stays within the caller's firm.
- `web-ui`: a new requirement for the `/admin/compliance` screen: what it must show, what it must never imply, and
  what it does when the chain is broken or the record is empty.

## Impact

- `src/Maf.Lab.Api/Endpoints/ComplianceEndpoints.cs` — one new paged, filtered read endpoint under the existing
  `firm-admin` policy.
- `web/src/compliance/*` (new), one nav entry, one route behind `RequireAdmin`, DTOs in `web/src/api/types.ts`.
- `docs/http-api.md`, `README.md`.
- No change to the chain, the export package, the audit writer or anything outside the admin area. Reading the
  record cannot alter it.
