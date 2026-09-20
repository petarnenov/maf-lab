# Proposal

## Why

Everything in the lab so far can be undone by closing the tab. The assistant reads, retrieves, explains and now
consults — but it has never *changed* anything, and the whole apparatus built for the moment it does is still
unused: the compliance reviewer has no production caller, the project's own convention about confirming writes has
never been exercised, and `DECISIONS.md` records MRTR `input_required` as "not exercised here".

A fee adjustment is the first write. It is worth doing carefully because it is where every guarantee the project
has claimed stops being theoretical: the tenant is the one in the token and not the one in the argument, the
reviewer's answer is data and not an instruction, the confirmation is the user's and not the model's, and
approving twice charges once.

## What Changes

- Accounts with fees exist. A read-only seed beside the billing runs gives each firm a handful of accounts with a
  current fee; the current fee of an account is that seed plus every adjustment applied to it.
- A new MCP tool, `propose_fee_adjustment`, honestly annotated as a write: not read-only, destructive, not
  idempotent. It is the first tool in the lab that is any of those.
- **Nothing is written on the first call.** The tool validates the proposal and returns MRTR `input_required`
  carrying a summary a person can check — the account, its current fee, the amount, the effect on the next
  invoice — and an opaque `requestState`. The write happens only on a second call carrying that state back.
- The `requestState` is **signed by the server**, so the account and the amount that come back are the ones that
  went out. The model sits between the two calls and cannot alter what will be executed.
- Applying an adjustment is durable and happens **at most once**: the ledger of applied adjustments lives in the
  MCP server's own small store, and the proposal's identity is the idempotency key. A repeated confirmation
  reports the adjustment as already applied and changes nothing.
- Above a configured threshold the adjustment must be reviewed by the compliance agent before the user is asked to
  confirm. Below it, the reviewer is not bothered. The reviewer's five outcomes each mean something different, and
  none of them is an exception: refused ends the flow, a question is put to the user and answered under the same
  review (at most twice), and a timeout, an unreachable reviewer or a failure ends the flow without a confirmation
  being offered.
- The verdict is treated as coming from a system this one does not control: its fields are validated, the account
  and adjustment it names must be the ones asked about, its text reaches the model only inside the data envelope,
  and an instruction embedded in it changes nothing about what executes.
- The API grows the server half of the confirmation: a `confirmation_required` event on the chat stream and an
  endpoint that approves or rejects. Approving re-issues the tool call with the state; rejecting tells the agent
  the user declined and it carries on without writing.
- Every step — proposed, reviewed, confirmed or rejected, executed — is recorded in the audit chain under its own
  kind, attributed to the person who acted, with identifiers only.
- **BREAKING** (internal): the MCP server no longer exposes only read-only tools. The "exactly three tools, all
  read-only" contract that the spec and the integration test encode is replaced by one that distinguishes the
  read-only tools from the write tool.

Not in this change, by the agreed split: the AG-UI event stream (`add-agui-stream`), the confirmation card and
session recovery in the browser (`add-confirmation-ui`), and the `/admin/a2a` screen with the A2A conformance and
UI-event eval sets (`add-a2a-admin-evals`). This change stops at the server contract; the browser still renders
what it renders today.

## Capabilities

### New Capabilities

- `fee-adjustment`: proposing, reviewing, confirming and applying a fee adjustment — what is written, what is
  never written without a person's word, and what happens when the same confirmation arrives twice.

### Modified Capabilities

- `retrieval-tool`: the server's tool inventory gains a write tool, so the "exactly three tools" and "all three
  read-only" requirements no longer describe it; annotations must now tell the truth per tool.
- `injection-defense`: the agent now has a tool with side effects, which the current requirement forbids outright.
  It is replaced by what actually protects the write — confirmation, a signed proposal, and a verdict that is data.
- `compliance-audit`: the recorded actions must include proposing, reviewing, confirming, rejecting and applying an
  adjustment, and a browsing filter must be able to find them.
- `compliance-review`: a verdict carries the account it concerns, so the caller can check that the answer is about
  the question.
- `chat-agent`: the event stream gains `confirmation_required`, and a turn can end waiting for a person.

## Impact

- **New**: `compose/seed/billing-accounts.json`; account and adjustment DTOs in `Maf.Lab.Domain/Billing`; an
  accounts reader and an adjustments store in `src/Maf.Lab.Retrieval/Billing/`; `FeeAdjustmentTools` in
  `src/Maf.Lab.Retrieval/Tools/`; the proposal signer; a fee-adjustment flow in `src/Maf.Lab.Api/Agent/` that
  drives the consultant and the confirmation; a confirm endpoint; a `retrieval-data` volume in compose.
- **Changed**: `ComplianceConsultant` gains its first production caller and two fixes it needs to be one — a
  timeout must keep the task id, and the adjustment id in a verdict must be the one that was sent, not the one
  that came back; `ChatTurnRunner`'s tool middleware learns the write tool; the system prompt gains it;
  `ReviewAgentHandler` echoes the account id; `evals/selection.jsonl` and the tool allowlist in
  `src/Maf.Lab.Eval/Datasets/Datasets.cs`; the "exactly three tools" integration test; the compliance page's kind
  filter, which is also missing the two A2A kinds added last change.
- **Dependencies**: none new. `Microsoft.Agents.AI.Workflows` is deliberately not taken (design.md says why), and
  no OpenTelemetry is introduced — the lab's own turn trace carries the steps, and that choice is recorded.
- **Risk**: the eval regression gate compares against a committed baseline, and new selection cases move the
  denominators; a new case the agent gets wrong fails the run rather than lowering a number quietly. That is the
  gate working, and it means this change ends with a deliberate baseline decision, not an automatic one.
