# Design

## Context

See proposal.md — Why. What exists and shapes this:

- **The interrupt already reaches the browser.** `toChatEvents` turns a paused `RUN_FINISHED` into a
  `confirmation_required` event, and `chatReducer` keeps it on the turn as `confirmation`. Nothing renders it.
- **Answering is a run.** `POST /api/chat` with `resume: [{ interruptId, payload: { approve } }]` applies or
  declines, and answers in the conversation as a normal text message. `ConfirmationService` already refuses an
  interrupt that belongs to someone else, has been answered, or is gone; the ledger applies once.
- **The proposal is durable.** `PendingAdjustments` holds the state, the summary shown, the status and the
  conversation, keyed by the adjustment id — which is the interrupt id. What it does not hold is the expiry or
  the question, both of which the card needs.
- **Conversations are already restored.** `/chat/:id` loads turns, tool calls, sources and feedback through
  `ConversationDetail`; `useChatStream.hydrate` replaces the chat with it.
- **Errors are one sentence.** `useChatStream` maps a status to text (`failureMessage`) and dispatches
  `stream_error`; `ChatPage` renders it the same way whatever happened.
- **Feedback is three kinds**, agreed in three places: `FeedbackKind` in the web, `FeedbackKinds` in
  `Maf.Lab.Domain`, and the review queue's filters.

## Goals / Non-Goals

**Goals:**

- A person can see a waiting write and answer it, in the conversation, without reading JSON.
- What they see is what the server would execute, and coming back later shows the same thing.
- An error tells them which of three situations they are in.
- The summary that a person approves can be judged, so it can be wrong on purpose in an eval rather than by
  accident in production.

**Non-Goals:**

- No re-attaching to a live run. A stream that was abandoned is over; what must survive is the proposal, and it
  does. Rebuilding a live run's event history would mean storing the run, which nothing else needs.
- No new confirmation mechanism. The protocol's resume is the only way to answer, as of the last change.
- No design system. Plain CSS modules, as the rest of the app.
- No admin screens or A2A conformance sets — the last change of the split.

## Decisions

### The card lives on the turn, and its state is the reducer's

`AssistantTurn.confirmation` already holds the interrupt. The card's own state — waiting, answering, applied,
declined, gone, expired — is added beside it rather than kept in the component, so a reload and a live run
produce the same thing and the reducer's tests can drive every state without rendering.

### Coming back reads the proposal, not the run

Opening a conversation asks `GET /api/conversations/{id}/pending` for what it is waiting on. This is the honest
equivalent of "reattach to the run": the run is gone, the proposal is not, and approving twice applies once
anyway. `ConversationDetail` could have carried it, but a separate call keeps the history endpoint about history
and lets the card refresh itself after an answer without reloading the whole conversation.

The endpoint needs two things the row does not store — the question a person was asked and the expiry — so both
are stored with the proposal when it is made. They were already computed: the tool composes the sentence and the
signer knows the expiry.

### Expiry is decided in the browser, and the server decides again

The card stops offering to answer once the expiry has passed, because asking a person to press a button that
cannot work is worse than telling them. The server still refuses an expired state, and that refusal is what the
card shows if the clocks disagree.

### Three faces, decided where the failure is known

`useChatStream` is where a failure's kind is known — a status code, a `RUN_ERROR`, a dropped connection — so it
dispatches a kind alongside the message rather than a sentence. `ChatPage` renders the kind. A recovered retry
is not an error at all and reaches the page only as the answer it produced.

The rule that no internal text is rendered is asserted against the rendered DOM, not against the strings the code
holds: the server's own error text is already short and user-facing, and the test's job is to prove the browser
does not add to it.

### The fourth feedback kind is added in the three places that must agree

`FeedbackKinds` in the domain (with its allowlist), `FeedbackKind` in the web, and the review queue's filter. The
button appears only on a turn that has a confirmation, because the kind is about a summary and a turn without one
has no summary to be wrong.

### The confirmation suite drives the real flow

`confirmation.jsonl` gives an account and an amount; the suite proposes through the same agent path the other
suites use, takes the summary from the interrupt, and checks it states the account, the amount and the resulting
fee the server itself computed. It is a faithfulness check with an arithmetic answer rather than a rubric — the
summary either says what would happen or it does not — so it needs no judge model.

## Risks / Trade-offs

- **A stored enum with three owners** → A round-trip test posts the new kind, reads it back from the queue and
  imports it, so the three places cannot drift apart silently.
- **The card is another thing that can disagree with the server** → It renders the server's own summary and its
  own question; the only thing it computes is whether the expiry has passed, and the server re-decides that.
- **A proposal endpoint is a new way to read a pending write** → It is scoped to the caller's own conversation
  and returns what the run already sent to that same person; the ownership check is the one the confirmation
  path already uses.
- **The suite adds a fifth dataset and a fifth metric to the gate** → It is deterministic, so a regression in it
  means the summary changed, which is exactly when someone should be told.
