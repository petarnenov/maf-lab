# Tasks

## 1. Keep the conversation on a failed run

- [x] 1.1 Add a reducer test: a streaming turn in conversation `c1` receives `done` with an error and an empty conversation id; assert `state.conversationId` stays `c1`, the turn is `status: 'error'` with the error text; verify it fails before the fix
- [x] 1.2 In `chatReducer.ts`'s `done` case keep the current conversation id when the event names none; verify the 1.1 test passes and the existing reducer tests stay green
- [x] 1.3 Map AG-UI `RUN_STARTED` to a stream event carrying the run's thread (the server-issued conversation id) and have the reducer take it, so a first turn that fails still knows its conversation; verify with a `chatEvents` test and a reducer test (fail first turn of a new conversation, then send: `conversationId` is the thread)
- [x] 1.4 Add a chat page test: a run on `/chat/:id` whose stream ends in `RUN_ERROR` shows `turn-error` under the question, does not refetch the conversation, and a following send uses the same thread id; verify `npm test` in `web/` is green

## 2. Verification

- [x] 2.1 Run `make lint`, the web test suite and `openspec validate fix-run-error-lost-in-ui --strict`; all green
- [x] 2.2 Rebuild the web container and reproduce with a failing model (api recreated without `OLLAMA_API_KEY`): the error shows under the question in the browser; restore the key afterwards
