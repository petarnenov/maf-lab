# Design

## Context

See proposal.md — Why. What exists and shapes this:

- **The turn is one writer into one channel.** `ChatTurnRunner.RunAsync` takes a `ChannelWriter<ChatEvent>` and
  writes everything into it in order: `TurnTrace` events from the moment the turn starts, `ToolCallStartedEvent`
  and `ToolCallFinishedEvent` from the function-invocation middleware, `TextDeltaEvent` from the model stream,
  `SourcesEvent`, `ConfirmationRequiredEvent`, and `DoneEvent` last. `ChatEndpoints.Stream` turns each into an
  SSE frame named by `ChatEvent.EventName`.
- **A lot happens between the model and the client.** The middleware audits every call, traces the arguments and
  the result, unpacks the MCP envelope, summarises the result per tool, and — for the write tool — runs the
  fee-adjustment flow, which may consult another agent for up to 90 seconds before the turn can continue.
- **The agent's stream is not a chat stream.** `agent.RunStreamingAsync` yields `AgentRunResponseUpdate`;
  `Microsoft.Agents.AI.AgentResponseExtensions.AsChatResponseUpdatesAsync` converts a stream of them into the
  `IAsyncEnumerable<ChatResponseUpdate>` that `AGUI.Server` consumes.
- **The SDK.** `RunAgentInput.ToChatRequestContext(...)` adapts an inbound run into the M.E.AI request shape and
  stashes the input on `ChatOptions.AdditionalProperties`; `AsAGUIEventStreamAsync(context, ct)` converts the
  response stream into `BaseEvent`s. `AGUIStreamOptions` carries the hooks: `MapCall(tool, …)`,
  `MapResult(tool, …)`, `MapInterrupt(content → AGUIInterrupt)`, `MapContent(…)`.
- **Three consumers of the stream today**: the web chat, the behind-the-scenes monitor (`trace` events, replayed
  by time travel), and `scripts/verify_lb.sh`, which asserts the SSE order through the balancer.

## Goals / Non-Goals

**Goals:**

- One wire format, defined by someone else, that a stranger's client could read.
- The pause-for-a-person modelled as the protocol models it, so approving is a run and not an endpoint.
- Everything the browser renders today still rendering, including the monitor and time travel.
- Cancellation that is real: the model stops, and no tool runs after it.

**Non-Goals:**

- No new UI. The confirmation card, session recovery and the error faces are `add-confirmation-ui`.
- No AG-UI client SDK in the browser. The web app reads SSE and maps events it knows; the protocol's own
  TypeScript packages would be a second dependency for a client that renders six event kinds.
- No agent-to-agent change. A2A is a different protocol for a different conversation and is untouched.
- No bidirectional transport. SSE stays; the protocol's other bindings are not needed here.

## Decisions

### The adapter maps the model's stream; the runner keeps everything else

The adapter wants `IAsyncEnumerable<ChatResponseUpdate>`; our runner produces events from four places, only one
of which is the model. Three ways to reconcile that:

1. **Hand-write the protocol.** Use `AGUI.Abstractions` types only and emit them ourselves everywhere. Rejected:
   the fiddly parts — when a text message opens and closes, message and tool-call identifiers, the ordering
   rules, what a terminal event may follow — are exactly what the SDK exists to get right, and the brief asks for
   the official SDK where a stable one exists. It does.
2. **Restructure the turn around the adapter.** Make the runner a thin producer of `ChatResponseUpdate`s and move
   audit, tracing, the envelope and the fee-adjustment flow behind `AGUIStreamOptions` hooks. Rejected: the trace
   begins before the model is called (turn start, intent, prompt, history) and the flow may block for a minute
   mid-call; neither is a mapping of a content item, and forcing them through mapping hooks would bend the
   pipeline around the adapter's shape rather than the other way round.
3. **Let the adapter own the model's output, inside the existing pipeline.** Chosen. The runner still owns the
   channel and the order. Where it used to write `TextDeltaEvent`, it now enumerates
   `AsChatResponseUpdatesAsync(agent stream).AsAGUIEventStreamAsync(context, ct)` and writes the adapter's events
   into the same channel as they arrive. Trace, sources and the interrupt are written by the runner around that,
   as protocol events of their own. One writer, one order, and the part of the protocol that is easy to get
   subtly wrong belongs to the SDK.

The tool-call events keep coming from the middleware rather than from the adapter's view of the model stream: the
middleware is where the call's summary, its audit and its result already are, and it is the only place that knows
a call was refused because a confirmation is pending.

### The channel carries `BaseEvent`, and `ChatEvent` goes away

`ChatEvent` and its six subclasses exist only to be named on an SSE frame. With the protocol's discriminator on
every event, the frame's name is the event's type and the payload is the event. `Maf.Lab.Domain/Chat/ChatEvents.cs`
keeps the request and conversation DTOs and loses the event hierarchy; the trace's own `TraceEvent` stays exactly
as it is and becomes the value of a custom event.

### Sources and the trace are custom events, named once

`maf-lab/sources` and `maf-lab/trace`. The protocol says a consumer may ignore a custom event it does not know,
which is what makes them safe to add; the web client keeps a small map from name to reducer action, and anything
else is ignored rather than dropped as an error. The monitor and time travel read the same `TraceEvent` payload
they read today, so nothing downstream of the reducer changes.

### The interrupt carries what the proposal already had

`AGUIInterrupt` has exactly the fields the confirmation needs: `Id` is the adjustment id, `Message` is the
sentence the tool already composes, `ResponseSchema` is the approve boolean the tool already declares,
`ToolCallId` ties it to the call that proposed it, `ExpiresAt` is the proposal's expiry — which until now was
known only to the signer — and `Metadata` carries the summary and the opaque state. Nothing new is invented; the
protocol had a slot for each of them.

Resuming is `RunAgentInput.Resume`: an `AGUIResume` naming the interrupt id with a payload saying approved or
declined. The endpoint routes a resume to `ConfirmationService`, which is unchanged underneath — it still checks
that the proposal belongs to this person, still calls the tool with the signed state, and the ledger still
applies once. `POST /api/chat/confirm` is deleted rather than kept as an alias: two ways to answer a proposal is
one too many, and the change is days old.

### Cancellation is a linked token, and the run is registered while it lives

A run is registered by its id in a per-instance table while it streams, so a stop request finds it and cancels
its token. Abandoning the stream cancels the same token through the request's own `RequestAborted`. The
middleware checks the token before invoking a tool, so "no tool executes after a stop" holds even when the
cancellation lands while the model is mid-decision. The table is per-instance and that is honest: a stop sent to
the replica that is not running the turn cannot stop it, the balancer spreads requests, and the gap is recorded
rather than solved with affinity that the rest of the API deliberately avoids.

### The web client maps, it does not adopt an SDK

`toChatEvent` becomes a map from the protocol's discriminator to the reducer's actions, with custom events routed
by name. The reducer's internal shape — turns, tool cards, sources, traces — does not change, so `chatReducer`,
the monitor and time travel keep their tests. An unknown event returns null and is ignored, which the protocol
allows and which the "an event the screen does not know" scenario now asserts.

## Risks / Trade-offs

- **A protocol swap with a live UI on the other side** → The web client moves in the same change, and the
  reducer's shape is deliberately untouched so its existing tests keep their meaning. `scripts/verify_lb.sh`
  moves with it, and a run through the balancer is part of the verification.
- **Two new packages for one wire format** → They are the protocol's own, MIT, stable, and one of them is
  literally an adapter for the chat abstraction this project already uses. The alternative was hand-writing the
  same mapping and maintaining it.
- **`Microsoft.Extensions.AI.Abstractions` 10.6.0 vs this project's 10.10.0** → NuGet resolves upward and the
  pinned version wins; a build that ever resolves downward would be caught by the solution build with warnings
  as errors.
- **Stop only reaches the replica running the turn** → Recorded. Abandoning the stream, which is what a browser
  actually does, always works because it is the same request.
- **The eval harness drives the same runner** → Its host consumes the run programmatically rather than over SSE,
  so it moves to the new stream in the same change and its suites are re-run.
