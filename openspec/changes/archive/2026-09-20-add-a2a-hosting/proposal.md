# Proposal

## Why

The assistant can be talked to by a person through the chat screen, and by a model through MCP tools. It cannot be
talked to by **another agent**. The team's next phase is A2A precisely so partner systems can ask the billing
assistant questions and hand it work, and today there is nothing for them to call: no agent card to discover, no
task to poll, no identity that is a *system* rather than a user.

This is the first of six changes for that phase. It makes the assistant an A2A server and nothing else — the
remote compliance sub-agent, the fee-adjustment write path, the AG-UI event stream, the confirmation card and the
admin screen each follow, and each depends on this one.

## What Changes

- The assistant is **served over A2A** at both transports the SDK maps: JSON-RPC and HTTP+JSON. gRPC is out of
  scope and the reason is recorded.
- An **Agent Card** at `/.well-known/agent-card.json` describes it: name, version, skills — each saying what it is
  *for* and what it is *not* for — the transports, and how to authenticate. The card is signed, and how to verify
  it is documented.
- An **extended card**, available only after authentication, adds one skill the public card must never show
  (`start_billing_run`).
- **Callers are systems, not people.** A partner's token is checked for this endpoint's audience, and what it may
  see is checked server-side against the firms that partner is entitled to. A question about another firm is
  *rejected* with a short reason and no data.
- **Two shapes of work.** A question that can be answered immediately comes back as a message. Starting a billing
  run comes back as a task: it reports progress, can be cancelled, can ask for a parameter the caller left out and
  resume when the caller supplies it, and ends with a structured artifact.
- **Task state lives outside the process**, so any replica can answer "where is my task" and a caller who lost its
  stream can resubscribe and miss nothing.
- **Push notifications**: a caller may register a webhook and receive exactly one delivery per state change.
- Every inbound request joins the audit record already in place — partner, operation, task, outcome, duration, and
  never the content of a message.

## Capabilities

### New Capabilities

- `a2a-hosting`: what the assistant offers other agents — discovery, who may call it and for what, the life of a
  task, what survives a lost connection or a replica change, and what a caller is told when it asks for something
  it may not have.

### Modified Capabilities

- `compliance-audit`: "Actions that must be recorded" gains the inbound A2A request, so a partner system's
  activity sits in the same chained record as a user's.

## Impact

- New `src/Maf.Lab.Api/A2A/*` and a mapped endpoint group; `A2A` and `A2A.AspNetCore` 1.0.0-preview2 added to
  `Directory.Packages.props`. The existing `ChatClientAgent` answers behind the SDK's handler — no second agent
  framework enters the host.
- `ITaskStore` backed by the shared SQLite the replicas already share (the SDK ships only an in-memory store).
- Partner identity and entitlements: an addition to the dev issuer and to `AuthOptions`, separate from user tokens.
- `MafDbContext`: task and push-configuration rows; `ToolAudit` gains the A2A kind.
- `compose/docker-compose.yml`, `scripts/verify_lb.sh`, `docs/http-api.md`, `README.md`, `DECISIONS.md`.
- No change to the chat contract, retrieval, tenancy enforcement or the web app. A partner's questions are answered
  by the same agent and the same tools, under a different identity.
