# Proposal

## Why

A write can be proposed, reviewed, paused on and applied — and none of it is visible. The run pauses with an
interrupt carrying the account, the fee, the amount and the expiry; the browser receives it, keeps it on the turn,
and renders nothing. The only way to approve an adjustment today is to post a run by hand.

The rest of what the browser owes a person is in the same state. An error is an error, whether the assistant
retried and recovered, the billing system is down, or the run belongs to someone else — one sentence for three
situations that call for three different reactions. Feedback has three buttons and none of them fits the thing a
confirmation can get wrong: a summary that reads correctly and describes the wrong adjustment. And a tab closed
mid-proposal loses nothing on the server — the proposal is a durable row — but the browser has no way to find it.

## What Changes

- **The confirmation card.** A run that pauses renders inline in the conversation, not as a modal: what the
  adjustment does to which account, what the fee is and would become, the period, and Approve / Reject. It says
  when the proposal stops being answerable, and stops offering the buttons once it has.
- Approving sends a run that resumes the interrupt; rejecting sends one that declines it. Both report what
  happened in the conversation, and the card settles into what it became — applied, declined, or no longer
  waiting.
- **A closed tab loses nothing.** Opening a conversation asks what it is waiting on, and a proposal still waiting
  comes back as the same card. The proposal, not the stream, is what survives — which is what the server already
  guarantees, since approving twice applies once.
- **Tool cards say what is happening**, including the long one: a compliance review reports that it is waiting and
  roughly how long it takes, rather than sitting silent for half a minute.
- **Three faces for three errors.** Something retried and recovered says little or nothing; something broken says
  what is broken and what to do; something refused says only that it was refused. No internal text reaches the
  page, which a test asserts against the rendered output rather than against the source.
- **A fourth feedback button**, for a confirmation summary that does not match what would be applied. It files
  like the others and lands in its own eval set, so the summary can be judged the way answers are.

Not in this change: the `/admin/a2a` screen, the A2A conformance scenarios and the recorded-stream eval sets —
those are `add-a2a-admin-evals`, the last of the split.

## Capabilities

### New Capabilities

- `write-confirmation-ui`: what a person sees when a write is waiting for them, what happens when they answer,
  and what they find when they come back to it later.

### Modified Capabilities

- `web-ui`: the chat screen renders a waiting write and the three faces of an error; feedback gains its fourth
  kind.
- `fee-adjustment`: what a conversation is waiting on can be asked for, so a proposal outlives the page that
  made it.
- `eval-harness`: the confirmation summary is a thing that can be wrong, so it has a dataset and a metric.

## Impact

- **New**: a confirmation card and its styles in `web/src/chat/`; the resume calls in `useChatStream`; an
  endpoint that reports what a conversation is waiting on; `evals/confirmation.jsonl` and its suite.
- **Changed**: `chatReducer` gains the card's states; `toolLabels` learns the review; the error paths in
  `useChatStream` and `ChatPage`; `FeedbackKind` and its server-side contract gain a fourth value;
  `docs/http-api.md`, README, `DECISIONS.md`.
- **Dependencies**: none. The protocol and the server contract both already carry everything this needs.
- **Risk**: the fourth feedback kind is a stored enum with a server-side allowlist and an eval dataset keyed by
  it; adding a value means the three places agree or the queue quietly drops it. A test covers the round trip.
