# Design: Keep the answer flow on the streaming pipeline

## Approach

Reuse the same answer-stream path that normal chat sends already use:

1. start a streaming assistant turn as soon as the answer action begins
2. send the resume request
3. read the SSE body with the existing stream reader
4. dispatch every parsed event into the reducer pipeline
5. finalize the confirmation state from the answer outcome exactly as before

This keeps the monitor panel open because the UI still sees an active assistant turn, and it keeps trace/tool rendering consistent because the same reducer logic handles both send and resume runs.

## Acceptance shape

- no separate "answer only" rendering path
- no hidden answer stream that bypasses the monitor
- existing confirmation copy and outcome semantics remain unchanged

## Testing

Add or update a regression test that clicks Approve/Reject and asserts the behind-the-scenes region stays present while the answer run streams, including at least one streamed tool event.
