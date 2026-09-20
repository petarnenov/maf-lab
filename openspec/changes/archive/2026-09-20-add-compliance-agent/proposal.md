# Proposal

## Why

The assistant can now be *called* by another agent. The other half of A2A — calling one — is where the interesting
failures live: a remote agent is slow, sometimes down, sometimes asks a question back, and is never something you
can simply `await` and forget. Until the assistant consults one, nothing in the lab exercises a dependency it does
not control.

A compliance review is the honest excuse for it. Before a fee adjustment is written, somebody who is not this
system has to agree — in a real TAMP, a compliance desk. That reviewer is a separate service with its own
identity, its own pace and its own right to say "not until you tell me why", which is exactly the shape a
sub-agent has.

## What Changes

- A new service, `Maf.Lab.ComplianceAgent`, in the same solution and its own container, behind the same balancer
  with two replicas. It exposes an A2A agent with **one** skill: review a proposed fee adjustment and return a
  verdict. It authenticates its callers the way the billing agent authenticates its partners — client credentials
  from the dev issuer, entitlements from configuration.
- The reviewer is deliberately unhelpful in lifelike ways: it takes tens of seconds, and about one review in five
  stops and asks for the advisor's justification instead of answering.
- The billing assistant consumes it as a **sub-agent** through the Agent Framework's A2A client: the remote card
  becomes an `AIAgent`, discovered from `/.well-known/agent-card.json` rather than from a hard-coded route.
- The assistant authenticates to the reviewer **as itself**, with its own client credentials. A user's token is
  never forwarded, and the reviewer never learns who asked.
- A review that times out, fails, or comes back asking a question is reported as that — never as a verdict, and
  never as a hang.
- Every outbound review is audited like every other action: who asked, which agent, which task, the outcome and
  the duration. Never the text.
- The A2A surface (`/a2a`, the well-known card) and the new compliance tier join the balancer's documented
  routing, which currently describes neither.

Not in this change: the fee-adjustment write tool itself, the multi-agent workflow that will orchestrate it,
confirmation-before-write, and any UI. Each is a later change in the agreed split.

## Capabilities

### New Capabilities

- `a2a-client`: consulting another agent over A2A — discovery from its card, this system's own credentials, and
  what happens when the remote agent is slow, absent, or asks a question back.
- `compliance-review`: what the reviewer offers and what a verdict is, including its right to ask for a
  justification and the fact that it decides nothing real.

### Modified Capabilities

- `compliance-audit`: the record must cover requests this system *sends* to another agent, not only those it
  receives.
- `load-balancing`: the entry point routes the A2A surface and the compliance tier, and that tier runs at least
  two replicas like the others.

## Impact

- **New**: `src/Maf.Lab.ComplianceAgent` (service + Dockerfile), `src/Maf.Lab.A2A` (the partner-identity, card and
  wire-format code both services need, moved out of `Maf.Lab.Api`), a compliance node in the topology report and
  diagram, a compose service and balancer routes.
- **Changed**: `Maf.Lab.Api` gains the sub-agent client and its configuration; `scripts/verify_lb.sh` and the CI
  end-to-end run cover the new tier; `docs/http-api.md`, README and `DECISIONS.md` gain the outbound side.
- **Dependencies**: `Microsoft.Agents.AI.A2A` moves from the probe tool into the API; `A2A.AspNetCore` is added to
  the new service. Versions stay pinned in `Directory.Packages.props`.
- **Risk**: the reviewer is slow by design, so the assistant's call needs a timeout that is shorter than a user's
  patience and longer than a healthy review; a wrong choice here shows up as a broken feature, not a slow one.
