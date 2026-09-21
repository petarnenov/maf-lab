# Proposal: Keep the behind-the-scenes panel open during answer runs

## Why

When the user answers a pending fee-adjustment proposal, the app currently shows the assistant's reply but the behind-the-scenes panel does not stay attached to the streaming answer run. That hides the live trace and tool activity for the resume path, even though the normal send path already keeps the panel open.

## What

Update the answer/resume flow so it behaves like a regular streamed chat turn from the monitor's point of view:

- start a streaming assistant turn before the answer request is sent
- keep the behind-the-scenes panel open while the resume stream is active
- route resume-stream events through the same reducer path used by normal chat sends
- preserve the existing confirmation outcome behavior and answer text in the transcript

## Outcome

Answering a proposal should keep the live trace visible while the assistant responds, so operators can see tool calls and trace events for the resume run just as they do for a normal chat turn.
