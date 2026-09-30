# Design

## Context

A chat turn builds its model client in `ChatTurnRunner` as `RequiredToolModeChatClient?(TracingChatClient(
OpenTelemetryChatClient(provider)))`, and hands it to a `ChatClientAgent`. The agent's streamed updates become the
AG-UI answer deltas, the stored answer and the conversation history. The A2A path (`AssistantBridge`) builds its own
`ChatClientAgent` over `models.CreateChatClient()` and replies with `response.Text`. Both are
`Microsoft.Extensions.AI.IChatClient` pipelines, and `DelegatingChatClient` is how this codebase already layers
behaviour onto them.

## Goals / Non-Goals

**Goals:** a deterministic removal that covers every answer surface, works on a stream, and never loses text.

**Non-Goals:**
- Asking the model not to write markers. That is a prompt change, which needs a version bump and an eval run, and
  would still be probabilistic.
- Rewriting answers already stored.
- Turning markers into links. The Sources list already links each source.

## Decisions

**A `DelegatingChatClient`, `CitationMarkerChatClient`, above the tracing client.** It sits at the one point every
answer passes through, before the agent, so SSE deltas, the stored answer, history and the answer check all see the
cleaned text. Above `TracingChatClient`, the trace's `model.response` still records what the model actually wrote,
and the monitor keeps that truth. The alternative, cleaning in the web renderer, would leave the stored answer, the
history, the answer check and A2A dirty.

**A small streaming state machine.** Text passes through at once until a `【`. From there it is held until `】`,
and the whole marker is dropped. Whitespace that ends the emitted text is held one step, so it can be dropped when a
marker follows, and emitted otherwise. If the held marker reaches 400 characters without `】`, it is released
unchanged. At the end of the stream, anything held is released. Only `TextContent` is touched. Reasoning content,
function calls, usage and finish reasons pass through in their updates. An update whose text becomes empty is still
yielded when it carries anything else, and the text released at the end is sent in an update with the last seen
`MessageId`, so it merges into the same message. The non-streaming path applies the same function to the response
messages' text.

**The count goes to the turn state.** The client reports each removal through a callback. The runner adds the
count to `turn.end` as `citationMarkersRemoved`. The A2A path uses the client without a callback, because it has
no turn trace.

## Risks / Trade-offs

- [A legitimate `【…】` in an answer (for example, quoting CJK text) is removed] → the corpus is English, and the
  domain has no such use. The count in the trace makes removals visible.
- [Holding text delays the stream] → only while a marker is open. Markers are under about 200 characters.
- [Old stored answers keep their markers] → deliberate: history shows what was answered.
