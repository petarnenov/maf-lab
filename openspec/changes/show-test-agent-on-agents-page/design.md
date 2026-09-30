# Design

## Context

See proposal.md for why. What exists today:

- `GET /api/admin/a2a` (`A2AAdminEndpoints`) returns firm-scoped inbound tasks, outbound consultations and push
  deliveries; `A2AAdminPage` renders them, with an early "Loading…" and an empty state when there is nothing.
- The api reaches the test agent through `TestAgentClient` (card discovery, client credentials, audited operations)
  and configures it with `Coverage.TestAgentOptions` (`BaseUrl`, `ClientId`, `ClientSecret`, `RunDeadline`,
  `MaxAttempts`, the model allowlist).
- `TopologyProbe` already fetches the agent's public card anonymously with a 2 s timeout, for the Topology screen.
- `GET /api/coverage/models` already computes the run limits (`RunLimitsDto`, via `TestGenRuns.AttemptBounds`,
  `RunLimits.*`, `TestGenRuns.DeadlineBounds`) — the same values the run picker shows.
- Runs are rows in `TestGenRuns` (SQLite), with `TestGenRunState` naming the states. Coverage is repository-wide; the
  Coverage API is not firm-scoped.
- The web app has no shared progress component yet, although `progress-feedback` requires one for long UI work.

## Goals / Non-Goals

**Goals:**
- One read-only endpoint with a purpose-built DTO that answers everything the section shows, in one round trip.
- The card check is bounded (timeout) and cheap (cached), so the endpoint answers well within 3 s even when the agent
  is down, and repeated opens do not hammer the agent.
- A reusable, themed progress indicator in `web/src/components`, used here.

**Non-Goals:**
- No actions on the test agent from this page (start, cancel, accept stay on the Coverage page).
- No authenticated call to the agent (the extended card, task listing): the public card is enough to prove it answers
  and to say who it is, and it needs no token.
- No change to Topology, the Coverage API or the test agent itself.

## Decisions

1. **Route: `GET /api/admin/a2a/test-agent`, in the existing FIRM_ADMIN group.** The page is admin-only and the group
   already carries that policy. Alternative: a route under `/api/coverage` — rejected, because the section belongs to
   the agents page, and `/api/coverage` reads are open to every role while this includes deployment details (the
   internal base address, the partner id).

2. **A `TestAgentProbe` in `src/Maf.Lab.Api/Coverage`** fetches `{BaseUrl}/.well-known/agent-card.json` anonymously with
   a timeout (`TestAgent:ProbeTimeout`, default 2 s) and caches the result in `IMemoryCache` for
   `TestAgent:ProbeCacheFor` (default 10 s). It parses the card as JSON (the spec's wire format, as the Topology probe
   does) into a `TestAgentCardDto`: name, description, version, skills, the JSON-RPC interface's URL and protocol
   version, the scopes from `securityRequirements`, and `capabilities.streaming`/`pushNotifications`. Failures map to
   a short reason ("timed out after 2 s", "connection refused", "HTTP 503", "not a card") and never a stack trace or
   exception message. Alternatives: reuse `TopologyProbe` — rejected, it probes the whole stack and returns display
   strings, not a card; reuse `TestAgentClient.ConnectAsync` — rejected, it needs a token and is audited per call,
   and a page view is not an operation on the agent.

3. **The overview is built in the endpoint from what the api already has.** Defaults come from the same helpers
   `/api/coverage/models` uses (`TestGenRuns.AttemptBounds`, `RunLimits.*`, `TestGenRuns.DeadlineBounds`), returned as
   the existing `RunLimitsDto`, so the section and the run picker cannot disagree. The default model is the allowlist
   entry marked `Default` (or the first one); availability is not checked here, since that asks the provider and
   costs a round trip per model. The budget default is stated as none (`defaultBudget: { maxTokens: null,
   maxCostUsd: null }`), which is what `TestGenRuns.StartAsync` does.

4. **Counts and recent runs in two queries.** A `GroupBy(State).Count()` over `TestGenRuns` for the counts (grouped
   into running, candidates, accepted, failed, other in code), and the ten rows with the latest `UpdatedAt` projected
   to `TestAgentRunDto` (id, path, state, reason, attempt, maxAttempts, lastPct, targetPct, model, updatedAt as UTC
   `DateTimeOffset`). No entity leaves the endpoint; no report, diff or activity text is read.

5. **No tenant filter, and no tenant parameter.** Runs are repository-wide (the Coverage API reads them unfiltered);
   the endpoint takes no parameter at all. Firm-scoped data (partner tasks) stays in the existing endpoint.

6. **Secrets.** The DTO carries `ClientId` and `BaseUrl` only. A test asserts the configured secret is not in the
   response body.

7. **Web: its own query and section.** `TestAgentSection` uses its own `useQuery(['admin','a2a','test-agent'])`, so it
   renders regardless of the activity's empty state and fails independently. `A2AAdminPage` keeps its header and
   Refresh always visible; Refresh refetches both queries and is disabled while either is fetching. Loading and
   refreshing show a new `Progress` component (indeterminate: a track and a moving bar in `--accent` on
   `--surface-2`; `role="progressbar"`, `aria-label`, `aria-valuenow/max` when determinate; no animation under
   `prefers-reduced-motion: reduce`). Styling reuses `Page.module.css` (`cards`, `card`, `big`, `table`, `tag`,
   `pass`, `fail`, `muted`, `mono`) so it matches the page in both themes; file links go to `/coverage?file=<path>`,
   which the Coverage page already reads.

8. **Progress for this page's work.** The work the page starts is two GETs. The overview is bounded by the probe
   timeout (2 s) plus two indexed SQLite queries, and the activity by its existing queries, so neither is expected
   to pass 3 s — but a slow disk or a cold container can, so both show the themed indeterminate indicator from the
   moment they start until they answer, then the data or an error naming what failed. There is no server step count,
   so indeterminate is the right form.

## Risks / Trade-offs

- [The card answers but the agent cannot run tasks (bad credentials, a broken runner)] → The section says
  "reachable", meaning the card answered; the Coverage page's start still reports `agent_unavailable`. The docs say
  that reachable means the card answered, not that the agent is healthy end to end.
- [A cached result hides a state change for up to 10 s] → The DTO carries `checkedAt`, shown beside the status.
- [Showing the internal base address] → The page is FIRM_ADMIN-only, and Topology already shows the same address to
  any signed-in user; it is not model-facing.
- [Several api replicas each cache their own check] → Harmless: at most one card fetch per replica per 10 s.

## Migration Plan

None: a new read-only route and a new page section. Rollback is reverting the commit.
