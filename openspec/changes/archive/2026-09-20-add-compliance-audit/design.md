# Design

## Context

See proposal.md for why. What it has to fit into:

- `ToolAudit.RecordAsync` is the single writer of `AuditRow` (`At`, `PrincipalId`, `FirmId`, `ConversationId`,
  `TurnId`, `ToolName`, `Arguments`, `Outcome`, `DurationMs`), called from the turn runner for a tool call and for a
  refused unknown tool. 65 rows exist, written before any of this.
- Two api replicas share one SQLite file (WAL). Any two of them can record an action at the same moment.
- `DatabaseInitializer` already adds missing columns to an existing database (`PRAGMA table_info` → `ALTER TABLE …
  ADD COLUMN`), so widening `AuditRow` needs no migration tooling.
- `AuthPolicies.FirmAdmin` guards `/api/admin/*`; the principal carries the firm, and `HistoryEndpoints` already
  derives ownership from it rather than from parameters.

## Goals / Non-Goals

**Goals:**
- A changed or missing audit row can be detected and pointed at.
- Deletion and export are recorded as first-class actions in the same ordered record as tool calls.
- An authorised person gets a scoped, self-checkable package without anyone opening the database.

**Non-Goals:**
- Making the record *immutable*. A hash chain proves tampering; it does not prevent it. Real immutability means
  append-only storage outside the application's reach (WORM bucket, external log service), which is a deployment
  decision, not a code one — stated in the spec's wording and in DECISIONS.
- Real identity. The dev token issuer still decides who "adam" is; the chain proves what was recorded, not that the
  recorded person is who they claim. That gap stays open and documented.
- Retention or erasure policy for turns and messages (the other compliance gap) — a separate change.
- Signing the chain with a secret. Considered and deferred: it defends against someone who can recompute the whole
  chain, but it adds a key to manage and, without external storage, that person can also change the head. The spec
  is written so adding an HMAC later changes no contract.

## Decisions

### One record, many kinds — not a second table
`AuditRow` gains `Kind` (`tool`, `conversation.delete`, `compliance.export`), and `ToolName` keeps its name but
holds the action for non-tool kinds (`conversation.delete`). A second table would need its own chain, and two chains
cannot order against each other — the whole value here is one sequence in which a deletion sits between the tool
calls that came before and after it. `Arguments` keeps its meaning: identifiers only, so an export row records the
range and counts, never a query or an answer.

### The chain: SHA-256 over the stored fields, linked by the previous row's hash
`Hash = sha256(PreviousHash + "\n" + canonical(row))` where `canonical` is the stored fields in a fixed order with a
fixed timestamp format. Verification recomputes it from the rows alone — no key, no side file. The first chained row
links to an empty previous hash, and the pre-existing 65 rows keep `Hash = null`: they are reported as "before the
chain" rather than silently treated as verified. Rewriting them would be the one thing an audit trail must never do.

**Concurrency is the real design constraint.** Two replicas appending at once must not both link to the same
predecessor. The append takes the previous head inside the same transaction and relies on SQLite's write lock:
`BEGIN IMMEDIATE` → read the last `Hash` by `Id` → insert → commit. Under WAL there is one writer at a time, so the
head cannot be read stale; a busy timeout covers the contention. This is also why the chain is over `Id` order, not
over `At`: clocks on two replicas can disagree, row ids cannot.

Cost: one extra read per audited action, in the same transaction that was already writing. The audit write is
already off the answer's critical path.

### Verification walks and reports, rather than returning a boolean
`GET /api/admin/compliance/verify` returns `{ intact, checked, from, to, head, firstBrokenId?, reason? }`. An
investigator needs to know *where* the record stops being trustworthy, because everything before that point is still
evidence. Scoped to the caller's firm; a firm cannot verify another's rows, and the chain covers all firms, so the
verification walks the global order and checks the links of the rows it is allowed to see, reporting the first break
it can attribute.

### The export is a JSON package with a manifest, produced by the same endpoint that records it
`GET /api/admin/compliance/export?from=&to=[&userId=]` returns one JSON document: `manifest`, `audit`, `turns`,
`conversations`. The firm comes from the principal. `userId` narrows; it cannot widen, and a user id from another
firm simply matches nothing.

The manifest carries `firmId`, `from`, `to`, `generatedAt`, `by`, per-section counts, `sha256` over the serialised
content sections, and `auditChainHead`. The digest lets a recipient re-hash the package; the chain head ties it to
the record it was drawn from, so a later verification shows whether the source changed after the export.

The export row is appended **before** the package is returned, and its own id is not inside the package (that would
be circular); the manifest carries the chain head as of the moment before its own row. Recording first means a
failed download still leaves the attempt recorded, which is the direction of error an auditor wants.

Format: JSON, not CSV or a zip. It is one document a recipient can hash and a test can assert on; the lab's volumes
are small enough that streaming and archiving would be complexity without a reader.

### Deletion records the person, not just the time
`ConversationRow.DeletedAt` stays as it is (the history behaviour depends on it). The deletion endpoint now also
appends an audit row with the principal, the conversation and the outcome. A refused deletion — someone else's
conversation — returns 404 as it does today and is **not** recorded as a deletion, since nothing was deleted; the
spec says so explicitly so that the absence is deliberate rather than an oversight.

### `(PrincipalId, At)` index
Added next to the existing `(FirmId, At)`. Both are needed: one for "what happened at this firm", one for "what did
this person do".

## Risks / Trade-offs

- **A hash chain is evidence of tampering, not protection from it** → stated in the spec and DECISIONS; the
  deployment-level answer (append-only external storage) is named as the next step, not pretended away.
- **Someone who can write the database can recompute the whole chain** → true, and the reason HMAC was considered;
  the honest mitigation is exporting regularly, since a package's manifest fixes the head at a point in time.
- **The chain is only as ordered as row ids** → ids are monotonic in SQLite; the chain never depends on clocks.
- **Contention between replicas** → `BEGIN IMMEDIATE` with a busy timeout; a test appends from parallel contexts and
  asserts the chain is intact.
- **Export size** → bounded by the period; the counts are in the manifest, and the endpoint is admin-only.

## Migration Plan

Additive: three nullable columns and one index, created by the existing initializer on start, on a live volume.
Existing rows stay untouched and unchained. Rolling back means ignoring the new columns; nothing else depends on
them.

## Open Questions

- Whether the export should eventually stream (or become a file the admin downloads asynchronously as an
  `AdminJob`) if a firm's volume grows — decidable from the counts the manifest now records, and it does not change
  the contract.
