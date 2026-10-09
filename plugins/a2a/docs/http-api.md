## A2A (partner systems, not users)

The assistant is also an [A2A 1.0](https://a2a-protocol.org) agent. A partner is a **system**, not a person: its
token's audience is the A2A endpoint, it carries no user identity, and what it may see comes from the server's
`A2A:Partners` registration — never from the request. To try it by hand, the a2a-inspector plugin (dev and qa) opens on
http://localhost:7172; the MCP endpoints can be tried the same way with the mcp-inspector plugin on http://localhost:7173
(see README, "Developer tools").

| Method | Path | Auth | Response |
|---|---|---|---|
| GET | `/.well-known/agent-card.json` | anonymous | the signed public agent card |
| POST | `/a2a/token` | anonymous | `{ accessToken, tokenType, expiresIn, scope }` for `{ clientId, clientSecret, scope? }` |
| POST | `/a2a` | partner | JSON-RPC 2.0 for every A2A method |
| POST/GET/DELETE | `/a2a/message:send`, `/a2a/message:stream`, `/a2a/tasks…` | partner | the same methods over HTTP+JSON |

Both transports carry the specification's own wire format: `message/send`, `tasks/get`,
`tasks/pushNotificationConfig/set`, `agent/getAuthenticatedExtendedCard`, roles `user`/`agent`, states such as
`input-required`, parts with their `kind`, and a result that *is* the task or the message. The preview SDK
underneath speaks a different dialect; `SpecWire` translates both ways and `DECISIONS.md` lists every divergence.

What a partner can do:

- **Ask about a run** — `message/send` with "status of run 4417" answers with a message, from the run's own record.
- **Ask anything else** — answered by the assistant itself, the same agent and the same MCP tools as the chat UI,
  scoped to the partner's firm.
- **Start a billing run** — a task, streamed over `message/stream`, ending in a `billing-run-result` artifact
  (a data part). The run is **simulated**: it walks the real lifecycle over seeded data and bills nobody.
- **Follow, resume, cancel** — `tasks/resubscribe` sends the whole current task first, so a dropped stream misses
  no transition; `tasks/cancel` stops a working task; a task in `input-required` continues when the caller sends
  the missing value under the same task id.
- **Be notified** — `tasks/pushNotificationConfig/set` registers a webhook, which receives one POST per state
  change carrying the task and the caller's own token in `X-A2A-Notification-Token`.

A request about a firm outside the partner's entitlement is rejected with one fixed sentence and no data — not the
firm, not the run, not whether either exists.

**Verifying the card.** The card carries a JWS in `signatures[0]`: `protected` is base64url JSON
(`{"alg":"HS256","typ":"JOSE"}`), and `signature` is base64url HMAC-SHA256 over
`protected + "." + base64url(canonical(card))`, keyed with the issuer's signing key. `canonical` is the served
card with its `signatures` member removed, every object's members sorted lexicographically, and no whitespace —
`json.dumps(card, sort_keys=True, separators=(",", ":"), ensure_ascii=False)` reproduces it, so a verifier never
has to guess this service's property order. `scripts/a2a_probe.py` does exactly that. In the lab the key is
symmetric, so verification needs the same secret; a real deployment would sign asymmetrically and publish the
public half (see `DECISIONS.md`).

## Agent to agent (TENANT_ADMIN only, otherwise `403`)

| Method | Path | Body | Response |
|---|---|---|---|
| GET | `/api/admin/a2a` | — | `{ inbound, outbound, deliveries }` |
| POST | `/api/admin/a2a/tasks/{id}/cancel` | — | `{ taskId, state }`, `404` unknown, `409` already finished |

`inbound` is one row per task a partner started — partner, operation, state, when it started and last changed,
how long it took, and whether it can still be cancelled. `outbound` is one row per consultation this system asked
of the reviewer, with its outcome. `deliveries` is every push-webhook attempt, with its attempts and its error.
No message content appears anywhere: the operation name, the state and the duration are what an operator needs.

Both are scoped by the caller's firm, taken from the principal. An inbound task carries the firm its partner was
entitled to act for, stamped when the task was created; a task belonging to another firm answers `404`, not
`403`, because its existence is not the caller's business. Cancelling goes through the same `CancelTask` a
partner's cancel does, and is itself audited as `a2a.cancel`.

The three store tables (`A2ATasks`, `A2APushConfigs`, `A2APushDeliveries`) are this plugin's, with their original names
and data. Removing the plugin is refused while a non-terminal task remains; `STOP_WORK=1` cancels through its owning
server first. The admin screen and its routes exist only while this plugin is installed.

Outbound system credentials are deployment configuration under `A2A:Clients:<agent>` (`BaseUrl`, `ClientId`,
`ClientSecret`), beside inbound `A2A:Partners:<id>`. A missing BaseUrl uses the installed plugin's bootstrap
`agent-card.json` JSON-RPC endpoint to find its card; the runtime still fetches the agent's authenticated card.
`A2A:StoreKeyspace` is required for each protocol host; the reviewer retains `compliance` and the test agent `testgen`.
