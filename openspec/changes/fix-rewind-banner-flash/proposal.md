# Proposal

## Why

While a turn streams, the chat flashes the time-travel banner ("⏪ Viewing step 48 of 73 · Return to now") with every
new trace event, most visibly while the model reasons. Nobody moved the cursor.

The cause is a one-render lag. The cursor follows the newest step by resolving to the reducer's `count`, and `count`
catches up with the events in an effect, after the render. For that one render the cursor is one step behind
`events.length`, so the chat treats the turn as rewound: the banner appears, the reasoning is rebuilt from the trace,
and the next render takes it all away again. Selecting another turn has the same lag for a cursor the user had pinned
on the previous turn.

## What Changes

- The time-travel state catches up with the events and with the selected turn during the render, not in an effect. A
  following cursor is therefore always the newest step.
- The chat shows a turn as of a step only when the user has moved the cursor (scrubber, step, jump or playback). A
  cursor that follows the newest step never rewinds the chat, whatever the events do.

## Capabilities

### New Capabilities
<!-- None -->

### Modified Capabilities
- `web-ui`: "Chat as of a step" applies only to a cursor the user moved; a following cursor never shows the banner.

## Impact

- **web:** `monitor/useTimeTravel.ts` (sync during render), `chat/ChatPage.tsx` (rewind only a moved cursor), tests.
- No api change, no packages.
