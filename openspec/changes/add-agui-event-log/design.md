# Design

## Context

See proposal.md — Why. What shapes the approach:

- The web client reads the SSE body in `readChatStream.ts`: parse frame → `toChatEvents` → reducer. `toChatEvents`
  is a total function from the protocol to this app's events, and returns `[]` for everything it has no use for.
- Live trace events already reach the panel through the chat reducer: `traceReducer` keys them by the client-side
  assistant turn id and `attach`es the server turn id when `done` arrives, so either id finds them.
- Stored traces come from `GET /api/turns/{turnId}/trace` via `useTurnTrace`, which uses the live events as
  placeholder data while the stored copy loads.
- On the server, every event of a run is written to one unbounded channel; `ChatEndpoints.Stream` reads that
  channel and yields one `SseItem` per event. That loop is the only place every frame of a run passes, including
  the `RunFinishedEvent` the cancellation path writes directly.
- The turn and its trace are persisted inside `ChatTurnRunner.RunAsync` *before* the terminal event is written, so
  a `TurnTraceRow` exists by the time the stream ends.
- `DatabaseInitializer` adds mapped-but-missing columns on start, so a nullable column costs no migration.
- A resume run (`ConfirmationService.ResumeAsync`) writes no `TurnRow` and no `TurnTraceRow`; its `RunFinishedEvent`
  carries no turn id.

## Goals / Non-Goals

**Goals:**

- One faithful, ordered list of what crossed the wire for a run — the frames this client drops included.
- The same list for a stored turn, without a second round trip beyond the trace fetch the panel already makes.
- No change to what is streamed, so no other consumer of the protocol is affected.

**Non-Goals:**

- Replaying or editing frames, exporting them, or diffing two runs.
- Recording frames for a run that produced no turn (a resume run) — live only, by spec.
- A second capture switch, a per-type filter, or coalescing repeated types. The user asked for the tab itself to
  be the switch and for a flat list; both are settled and not revisited here.

## Decisions

### Capture in `readChatStream`, not in `toChatEvents`

`readChatStream` holds the raw frame text before it is parsed, so it can record the byte size, keep the payload of
a type the client does not handle, and record a frame whose JSON does not parse at all — none of which
`toChatEvents(BaseEvent)` can see, since it is only reached for frames that already parsed into an object.
`toChatEvents` stays what its comment says it is: a mapping from the protocol to this app's events.

`readChatStream` gains a second callback, `onFrame(frame)`, called once per frame before the mapped events are
emitted. `useChatStream` turns it into a `{ type: 'agui_frame' }` chat event and dispatches it through the same
reducer, so the replay test in `recordedRuns.test.ts` exercises the new path with no change to how it drives the
stream.

*Alternative:* have `toChatEvents` prepend an `agui_frame` event to every mapping. Rejected — it makes a pure
mapping stateful about sequence numbers and blinds it to unparseable frames.

### Frame shape

`{ seq, atMs, type, name?, bytes, traceSeq?, payload?, unparsed?: string }`. `seq` is arrival order within the run
starting at 1; `atMs` is milliseconds since the run's first frame. `name` is set only for a custom event.
For a `maf-lab/trace` frame the client reads the trace event's `seq` into `traceSeq` and drops `payload` —
the trace event itself is already in the panel's other five tabs, and copying it would roughly double what the
turn holds in memory and on disk. `unparsed` holds the raw text of a frame whose JSON did not parse.

### Frame state lives beside the trace state

`traceReducer`'s state gains `framesByKey`, alongside `byKey`. The keying, the `attach` on `done` and the `reset`
are exactly the trace's, and duplicating that bookkeeping in a second reducer would be the only alternative.
`traceFor(state, keyOrTurnId)` gets a sibling `framesFor(state, keyOrTurnId)`.

### Server records in the SSE loop and writes once, after the stream

`ChatEndpoints.Stream` records each event as it yields it, then, in a `finally`, persists what it recorded. The
turn id comes from the `RunFinishedEvent`'s result; with no turn id — a resume run, or a run the client abandoned
before it finished — nothing is written, which is what the spec asks for.

*Alternative:* wrap the `ChannelWriter<BaseEvent>` handed to the runner. Rejected — the recorder would then hold
events the client never received when it walks away mid-stream, and the wrapper has to be threaded through both
the runner and the cancellation path to see every frame that the loop sees for free.

### The frames ride on `TurnTraceRow`

A nullable `AguiJson` column on `TurnTraceRow`, updated by turn id after the stream ends. Retention
(`TraceRetentionService` deletes the row), access (`TraceEndpoints` already decides who may read it) and delivery
(the same response) all come from the row that is already there, and `DatabaseInitializer` adds the column on
start. `TurnTraceDocument` gains `aguiFrames: AguiFrame[] | null`, where `null` means "not recorded" — which the
tab must say rather than showing an empty run.

*Alternative:* a `RunFrameRow` table keyed by turn id. Rejected — it buys a smaller trace response for the review
queue at the cost of a second access rule, a second purge and a second read, for a payload the 256 KB cap already
bounds.

### Trace frames are recorded by reference on the server too

The server records a `maf-lab/trace` frame as name + the trace event's `seq` + its serialized size. Without this,
storing the frames would store a second copy of the whole trace inside the same row.

### Live frames win over stored frames for a turn still on screen

The panel prefers the frames the client captured for the selected turn and falls back to the stored ones, exactly
as `useTurnTrace` already prefers live trace events through `placeholderData`. The client's copy is what actually
arrived — including anything malformed, which the server's copy cannot show.

### Cursor mapping in the AG-UI tab

The cursor is over trace events. A recorded trace frame carries that event's `seq`, so the tab finds the frame
whose `traceSeq` equals the cursor's trace event `seq`, treats every frame up to and including it as reached,
highlights it, and dims the rest. Cursor 0 ("before the turn started") reaches nothing. This needs no wall-clock
comparison and works identically for live and stored frames.

## Risks / Trade-offs

- **Memory in a long conversation**: every frame of every turn is kept for the session, and a long answer is
  hundreds of content frames. → Content frames are small (a delta and an id); the tab is per turn, and a session's
  turns are already held with their traces. If it bites, the cap that bounds the stored copy can bound the live one.
- **The trace response grows for every reader, including `/admin/feedback`**: → bounded by the 256 KB cap, and the
  frames are `null` for every turn recorded before this change.
- **A second copy of answer text at rest** (the trace already holds `answer.delta` chunks): → same row, same
  retention, same access scope, so nothing new is exposed and nothing outlives the trace. Frames stay out of logs.
- **Client and server lists can differ** (the client's is what arrived, the server's what it sent, and a run the
  client abandons stores nothing): → the tab says which copy it is showing, and prefers the client's while it has it.
- **Payload-less trace rows may read as a gap**: → the row names the trace event and its sequence, and the other
  tabs hold the data.

## Migration Plan

Additive. `AguiJson` is nullable and added on start by `DatabaseInitializer`; turns recorded before this change
report `aguiFrames: null` and the tab says they were not recorded. Rollback is removing the tab and the recorder —
the column can stay, unread.
