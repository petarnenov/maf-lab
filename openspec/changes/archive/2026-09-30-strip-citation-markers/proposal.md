# Proposal

## Why

gpt-oss writes its own inline citation markers into answers: `【tool_data†get_billing_run_status】`,
`【sourcePath: procedures/missing-fee-schedule.txt, Section 1 → Step 1】`, or a quoted passage between `【` and `】`.
68 of the 328 stored answers (21%) contain them. The UI renders them as plain text in the middle of a sentence, and
they are now visible in the README's hero screenshot. The sources of a turn are already shown as structured
**Sources**, with a link and the section, so the markers add nothing a reader can use. They also go to A2A callers,
into the conversation history the model reads on the next turn, and to Jev's answer check.

## What Changes

- The api removes every `【…】` marker from the model's answer text before anything downstream sees it: the
  streamed deltas, the stored answer, the conversation history, the answer check and the A2A reply. It removes the
  whitespace that led into a marker too, so no dangling space is left before punctuation.
- This works on the stream: a marker split across deltas is held back until it closes. An unclosed `【` longer than
  a bound is released as ordinary text, so a stray bracket is never swallowed.
- The model's reasoning text is left as it is.
- The turn's trace records how many markers were removed, so the monitor still shows that the model wrote them.
- Answers stored before this change are not rewritten. History shows them as they were given.

## Capabilities

### New Capabilities
<!-- None. -->

### Modified Capabilities
- `chat-agent`: a new requirement that answers carry no provider citation markers, on every surface the agent answers
  through.

## Impact

- `src/Maf.Lab.Api/Agent/` (a new chat-client layer in the turn's pipeline, wired in `ChatTurnRunner`), and the A2A
  `AssistantBridge`.
- `turn.end` in the trace gains a `citationMarkersRemoved` count; `docs/trace-events.md` documents it.
- No prompt change, so no prompt version bump and no eval baseline move. Retrieval, tools and sources are untouched.
- After this lands, the README's chat screenshot is re-taken (`make screenshots SHOTS=chat`).
