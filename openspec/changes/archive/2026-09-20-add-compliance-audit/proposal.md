# Proposal

## Why

The audit trail records the right facts and nothing it should not — 65 rows, argument identifiers only, no message
content — but it cannot yet do the job an audit trail exists for:

- **It cannot be trusted.** `Audit` is an ordinary table. Anyone who can reach the SQLite volume can edit a row or
  delete one, and nothing anywhere would show it. A record that can be changed by the party it describes is not
  evidence.
- **It does not cover the actions that matter most.** Deleting a conversation writes `DeletedAt` and nothing else:
  no record of who asked, or when. Only tool calls are audited, so the two things an investigator would ask about
  first — data destruction and data extraction — leave no trace at all.
- **Answering a request is a manual SQLite session.** There is no way to hand an authorised person a scoped,
  checkable package; today it is hand-written queries against a copy of the database, with no record that the
  extraction happened.
- **It is indexed for the wrong question.** `(FirmId, At)` answers "what happened at this firm"; the first question
  in any investigation, "what did this person do", has no index.

## What Changes

- **Every audit row is chained.** Each row carries a hash over its own content and the previous row's hash, so a
  changed or removed row breaks the chain at a point that can be named. A verification endpoint walks the chain and
  reports where it first breaks.
- **Deletion and export become audited actions.** The audit stops being a tool log and becomes an action log: a tool
  call, a conversation deletion, and a compliance export are all rows in the same chain, distinguished by kind.
- **A compliance export.** A FIRM_ADMIN can download a package for a period — audit rows, turns and conversations —
  scoped to their own firm by the token, never by a parameter. Passing a user id narrows it to that subject, for a
  data subject request. The package carries a manifest: who produced it, when, the range, the counts, a digest of
  the content and the audit chain head, so the recipient can check it was not altered after the fact.
- **The export is itself audited**, in the same chain: extracting data is an action, and it is the action an
  investigator is most likely to ask about later.
- **An index by principal**, so "what did this person do" is a query rather than a scan.

## Capabilities

### New Capabilities

- `compliance-audit`: the tamper-evident action log and what it must contain, how it is verified, and what an
  authorised person can be handed when they ask.

### Modified Capabilities

- `chat-agent`: "Tool audit log" becomes the general action log — tool calls remain exactly as specified, and
  deletion and export join them in the same record.

## Impact

- `src/Maf.Lab.Api/Storage/MafDbContext.cs` — `AuditRow` gains `Kind`, `Hash`, `PreviousHash` (additive columns, the
  existing initializer handles them) and an index on `(PrincipalId, At)`.
- `src/Maf.Lab.Api/Agent/ToolAudit.cs` — becomes the writer of every audited action and computes the chain link.
- `src/Maf.Lab.Api/Endpoints/HistoryEndpoints.cs` — deletion records who asked and when.
- `src/Maf.Lab.Api/Endpoints/` — a new compliance endpoint group (export, verify) under the existing `firm-admin`
  policy.
- `docs/http-api.md`, `README.md`, `DECISIONS.md`.
- No change to retrieval, tenancy enforcement or the chat contract. The 65 existing rows are not rewritten: the
  chain starts where it starts, and the report says so.
