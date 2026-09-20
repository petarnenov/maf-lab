# Proposal

## Why

The browser and the API agree on a protocol that exists nowhere else: six event names invented for this lab, a
`KNOWN` set in the client that silently drops anything it has not heard of, and a turn that ends waiting for a
person expressed as an event called `confirmation_required`. It works, and it is entirely ours — which means every
future consumer, every recorded stream and every tool that might read it has to learn our dialect first.

AG-UI is the protocol for exactly this: an agent's run, streamed to a front end. It has a stable 1.0 schema and,
since three days ago, an official .NET SDK — `AGUI.Server` / `AGUI.Abstractions` 1.0.0, MIT, published by the
AG-UI Protocol organisation, whose server package describes itself as an adapter that converts
`Microsoft.Extensions.AI` chat streams into AG-UI event streams. That is this project's chat loop, named.

It also has something the Day-4 brief did not anticipate. The brief planned to carry confirmation-before-write as
"a custom event for `confirmation_required`". The protocol already models it: a run that needs something from
outside **pauses** with an `AGUIInterrupt` — an id, a message, a response schema, the tool call it belongs to and
an expiry — and the answer arrives as an `AGUIResume` on the run that continues from it. That is a better shape
than the one shipped last change, and adopting it is most of why this change is worth doing now rather than later.

## What Changes

- The chat endpoint speaks AG-UI. It accepts a `RunAgentInput` — `threadId`, `runId`, `messages`, and optionally
  `resume` and `state` — and streams AG-UI events. `threadId` is the server-issued conversation id bound to the
  principal; a `threadId` belonging to someone else is still not found.
- The run's shape becomes the protocol's: `RunStarted` … `RunFinished`, or `RunError` when it fails. The answer is
  `TextMessageStart` / `TextMessageContent` / `TextMessageEnd`. A tool call is `ToolCallStart` / `ToolCallArgs` /
  `ToolCallEnd` / `ToolCallResult`.
- What the protocol has no word for travels as `CustomEvent`, its own extension point: the sources panel's data
  and the behind-the-scenes trace. The monitor and time travel keep working unchanged.
- **A write waiting for a person becomes an interrupt.** The run finishes with an interrupt outcome carrying the
  proposal's id, the sentence a person reads, the schema of the answer, the tool call it belongs to and the
  proposal's expiry. Approving or rejecting is a new run whose `resume` answers that interrupt.
  **BREAKING:** `confirmation_required` and `POST /api/chat/confirm`, both added last change, are replaced by
  the interrupt and the resume. There is one way to answer a proposal, and it is the protocol's.
- Cancelling is first class: closing the stream or asking for the run to stop ends it within a second, and no
  tool executes after that.
- The web client moves onto the new events and keeps doing exactly what it does today — streaming answers, tool
  cards, the sources panel, the monitor, time travel. No new behaviour; the card that renders a confirmation is
  the next change.

Not in this change, by the agreed split: the confirmation card, session recovery after a closed tab, the three
faces of an error and the fourth feedback button (`add-confirmation-ui`); the `/admin/a2a` screen and the
conformance, verdict-injection and UI-event datasets (`add-a2a-admin-evals`).

## Capabilities

### New Capabilities

- `agui-stream`: the protocol between the API and whatever is watching a run — how a run starts, streams, pauses
  for a person and ends, and what carries the parts of this system the protocol does not name.

### Modified Capabilities

- `chat-agent`: the SSE event list moves out of this capability. What remains is the agent's own behaviour;
  the wire is `agui-stream`'s business.
- `fee-adjustment`: a waiting write is an interrupt on the run rather than an event of our own, and it is
  answered by resuming the run rather than by a separate endpoint.
- `turn-tracing`: the live trace rides the protocol's extension point, and says so.
- `web-ui`: the chat screen renders from AG-UI events; the tool card and sources scenarios name them.

## Impact

- **New**: `AGUI.Server` and `AGUI.Abstractions` 1.0.0 pinned in `Directory.Packages.props`; an AG-UI layer in
  `src/Maf.Lab.Api/Agent/` that turns a run into protocol events; run cancellation.
- **Changed**: `ChatTurnRunner` streams AG-UI events instead of `ChatEvent`s; `ChatEndpoints` accepts
  `RunAgentInput` and handles `resume`; `ConfirmationService` is reached through a resumed run;
  `Maf.Lab.Domain/Chat/ChatEvents.cs` gives up the invented event names; the web's `chatEvents`, `readChatStream`,
  `chatReducer`, `useChatStream` and the monitor's reducer move to the protocol; `docs/http-api.md` and
  `docs/trace-events.md`.
- **Dependencies**: two new packages, both recorded in `DECISIONS.md`. They depend on
  `Microsoft.Extensions.AI.Abstractions` 10.6.0, below this project's pinned 10.10.0, which resolves upward.
  `Microsoft.Agents.AI` stays: `AsChatResponseUpdatesAsync` is the bridge from the agent's stream to the shape the
  adapter consumes.
- **Risk**: this is a protocol replacement with a live UI on the other side. Everything the browser renders today
  has to keep rendering, which is why the web client moves in the same change rather than the next one. The eval
  harness drives turns through the same runner, so its host moves too.
