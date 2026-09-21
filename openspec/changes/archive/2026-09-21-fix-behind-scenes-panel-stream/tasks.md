# Tasks

## 1. Keep answer runs streaming

- [x] 1.1 Update `web/src/chat/useChatStream.ts` so the answer/resume path opens a streaming assistant turn before the request is sent.
- [x] 1.2 Route resume SSE events through `readChatStream` and the normal chat reducer path so monitor events remain visible.

## 2. Add regression coverage

- [x] 2.1 Extend the confirmation flow tests to assert the behind-the-scenes panel remains visible during an answer run.
- [x] 2.2 Include a streamed tool event in the resume response to prove trace/tool events reach the monitor.

## 3. Verify behavior

- [x] 3.1 Run the affected web tests.
- [x] 3.2 Run the web build and lint targets.
