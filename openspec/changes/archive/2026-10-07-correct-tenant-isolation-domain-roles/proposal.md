# Proposal

## Why

The tenant-isolation spec's scenario "Valid token yields principal" says the billing server scopes its results to the
token's advisor ids. No server does: `domain_roles` and `advisor_ids` are defined (`PrincipalClaims`) and issued (the dev
issuer's personas), but nothing outside the tests reads them (found by the adopt-company-idp survey, 2026-10-07). A spec
that asserts behaviour the code does not have misleads every change that relies on it. The user chose to correct the
spec now.

## What Changes

- `tenant-isolation`'s "Principal derived from token": its scenario no longer claims that billing scopes by advisor ids.
  It says what is true: domain roles and advisor ids travel in the token for the domain's own server to read, the core's
  principal holds none of them, and no server reads them today.
- DECISIONS §86 records billing advisor scoping (`domain_roles`, `advisor_ids`) as not implemented, a future billing-plugin
  change.

No code changes.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `tenant-isolation`: "Principal derived from token" says what the code does with domain roles.

## Principles

None — spec text only, no code or design: the spec is made to state behaviour the code has (OpenSpec's source of
truth), and the gap is a follow-up in DECISIONS §86.

## Progress

None — spec text only; nothing runs.

## Stopping

None — spec text only; nothing runs.

## Documentation impact

- `openspec/specs/tenant-isolation/spec.md` (through this change's delta) and DECISIONS §86.
