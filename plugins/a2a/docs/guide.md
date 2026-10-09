## Another agent talking to this one (A2A)

The assistant is also an **[A2A 1.0](https://a2a-protocol.org) agent**, so another system can work with it without
a person in the loop. It advertises itself at `http://localhost:7171/.well-known/agent-card.json` — a signed card
naming its skills, its transports (JSON-RPC and HTTP+JSON) and how to authenticate — and answers on `/a2a`.

A partner is a **system, not a user**: its token's audience is the A2A endpoint, it carries no user identity, and
the firms it may see come from the server's `A2A:Partners` registration rather than from anything it sends. Ask
about a firm outside that set and you get one fixed sentence and no data — not the run, not whether it exists.

It can ask about a billing run, ask anything else (answered by the same agent and the same MCP tools the chat UI
uses), or **start a billing run** — which comes back as a task it can follow, resume when the agent asks for a
missing period, resubscribe to after a dropped connection, cancel, or be notified about through a webhook. That
run is **simulated**: it walks the real lifecycle over the seeded runs and bills nobody. The card says so, the
final artifact says so (`"simulated": true`), and so does this paragraph.

Two clients prove it from outside: `python3 scripts/a2a_probe.py` speaks the 1.0 wire format and imports no A2A
library at all, and `make eval-a2a` runs `tools/Maf.Lab.A2AProbe`, which builds an agent from nothing but the card
using the A2A client. The preview SDK underneath does not yet speak 1.0 on the wire; every difference and what is
done about it is in `DECISIONS.md`.

**Conformance is a dataset, run by an outside client.** `evals/a2a-conformance.jsonl` names what such a client
must be able to do — discovery, the extended card after authenticating, a direct answer, a streamed task,
resubscribing after a dropped stream, resuming a task that asked for something, cancelling, a push delivery to a
webhook it registered, and a request outside its entitlement being refused. The probe reads that file and runs
the scenario each row names; it has no project reference to `src/`, which is what makes it evidence rather than a
self-assessment. It writes a report in the same shape every eval suite writes, to `evals/reports/`, and
`make ci-e2e` runs it. `make eval SUITE=…` does **not** — that target dispatches into the harness, which links
against the service.

**`/admin/a2a`** (TENANT_ADMIN) is where that traffic is visible: what arrived from partners, what this system
asked of the reviewer, and every push delivery — each with its state, when it happened and how long it took. A
task still running can be cancelled from there, through the same protocol call a partner would make. The test-generation agent and its runs are shown on the Coverage page.
