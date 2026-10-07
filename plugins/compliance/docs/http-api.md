#### The audit screen (TENANT_ADMIN only, otherwise `403`)

Over the core's audit record (`IAuditTrail`): the record and its chain are the core's, the screen is this plugin's.

| Method | Path | Query | Response |
|---|---|---|---|
| GET | `/api/admin/compliance/verify` | — | `{ intact, checked, unchained, from, to, head, firstBrokenId, reason }` |
| GET | `/api/admin/compliance/actions` | `from`, `to`, `userId`, `kind`, `limit` (≤200, default 50), `before` | `{ actions, nextCursor }` |
| GET | `/api/admin/compliance/export` | `from`, `to`, optional `userId` | `{ manifest, conversations, turns, actions }` |

Every audited action carries `hash` = SHA-256 over the previous action's hash and its own stored fields, so a
changed or removed row breaks the chain; `verify` walks it by row id and names the first row that does not hold.
`unchained` counts rows written before chaining began — they are reported, never rewritten.

`actions` reads the record newest first, paged by row id: pass the previous page's `nextCursor` as `before` for the
older ones, until it is null. Reading is never recorded — browsing the log must not grow it.

The export's tenant comes from the token; a `firmId` or `tenantId` parameter is ignored, and `userId` only narrows (a data subject
request). Deleted conversations are included, carrying `deletedAt`. The manifest is
`{ tenantId, subjectUserId, from, to, generatedAt, by, counts, sha256, auditChainHead }`.

`sha256` covers the three content sections in a **canonical rendering**, not this service's JSON output, so any
recipient can recompute it: the literal section names `conversations`, `turns`, `actions` each on their own line,
followed by one line per row, fields joined with `U+001F` in the order the DTOs declare them, timestamps as UTC
`yyyy-MM-ddTHH:mm:ss.fffZ`, booleans as `true`/`false`, nulls as the empty string, rows terminated by `\n`.

#### The compliance reviewer (a second agent)

A separate service, behind the same entry point under `/compliance`, with its own identity and its own card. It is
what the assistant consults before a fee adjustment is applied.

| Method | Path | Auth | Response |
|---|---|---|---|
| GET | `/compliance/.well-known/agent-card.json` | anonymous | the signed card: one skill, `review_fee_adjustment` |
| POST | `/compliance/a2a/token` | anonymous | a token for **this** agent's audience, scope `a2a.compliance.review` |
| POST | `/compliance/a2a` | partner | JSON-RPC, and the HTTP+JSON paths beside it |

Send the adjustment as a **data part** — `{ adjustmentId, firmId, accountId, amount, reason }` — and the review
comes back as a task: progress while it works, then a `compliance-verdict` artifact carrying
`{ adjustmentId, decision, reason, reviewedAt, simulated }`. About one review in five stops in `input-required`
and asks for the advisor's justification; sending it under the same task id continues that review. Anything that
is not a fee adjustment is answered with one sentence and no verdict.

The review is **simulated**: a threshold (`Review:RefuseAboveAmount`) and a stopwatch
(`Review:MinDurationMs`/`MaxDurationMs`, 20–60 s in the stack), with `Review:AskForJustificationRate` deciding how
often it asks first. It binds nobody.

**How the assistant authenticates to it.** With its own client credentials (`Compliance:ClientId` /
`Compliance:ClientSecret`), exchanged at the reviewer's own token endpoint. A user's token is never forwarded: the
audiences differ, so a token for `/a2a` is refused at `/compliance/a2a` and the other way round. The reviewer is
found by fetching its card from `Compliance:BaseUrl` — the card says where it answers, and nothing else is
hard-coded. A consultation ends as a verdict, a question, a timeout (`Compliance:Deadline`), an unreachable agent
or a failure, and each one is written to the audit record as `a2a.consultation` — agent, adjustment id, task id,
outcome and duration, never the content. When the chat run that asked is stopped while a review is in flight, the review is
cancelled with A2A `tasks/cancel` (not on the deadline, which keeps the task to collect later), recorded the same way
as `a2a.consult.cancel`; the cancel stops the review whichever reviewer replica runs it, because the replicas' shared
task store is where it is recorded.
