# Tasks

## 1. Reducer — narrow `answered` to confirmation state only

- [ ] 1.1 In `web/src/chat/chatReducer.ts`, remove the `said` field from the `answered` action type and
  remove the new-turn-creation block from the `answered` case. The case now only calls `updateConfirmation`.
  Verify: TypeScript compiles with no errors (`cd web && npm run build`).

- [ ] 1.2 Remove the `outcomeOf` helper from `useChatStream.ts` and any import of it, OR keep it as a local
  closure inside `answer()` (see task 2.2). Verify: no unused-variable lint errors (`make lint-web`).

## 2. `useChatStream.answer()` — route through the event pipeline

- [ ] 2.1 Before the POST, dispatch `{ type: 'send', userTurnId: nextId('u'), assistantTurnId: nextId('a'), text: '' }`.
  This sets `streaming: true` and inserts a streaming assistant turn that the monitor will follow.
  Verify: after clicking Approve the Send button is disabled while the answer streams.

- [ ] 2.2 Replace the private `said` accumulator with a closure that both (a) forwards each event to
  `dispatch({ type: 'event', event })` and (b) accumulates text deltas for `outcomeOf`. The
  `readChatStream` call becomes `readChatStream(body, event => { dispatch({ type: 'event', event }); if (event.type === 'text_delta') said += event.data.text; })`.
  Verify: after approving, the conversation shows the server's confirmation text as a normal assistant turn.

- [ ] 2.3 After `readChatStream` resolves, dispatch `{ type: 'answered', adjustmentId, outcome: outcomeOf(said, approve) }` (no `said` field).
  Verify: the confirmation card changes state (Applied / Declined / gone) after the stream ends.

- [ ] 2.4 In the error paths (`!response.ok`, `catch`), dispatch `{ type: 'answered', adjustmentId, outcome: 'gone' }` as before,
  but also dispatch a `stream_error` (or `answered` only — gone is already shown on the card).
  Verify: killing the server while an answer is in flight leaves the card in 'gone' state, not stuck on 'answering'.

## 3. Tests — assert monitor panel behaviour

- [ ] 3.1 In `web/src/chat/ChatPage.confirmation.test.tsx`, extend the `propose()` helper so the second fetch
  (the resume run) returns a `streamResponse` that includes at least one `run.started()`, one tool call pair
  (`run.toolStarted` / `run.toolFinished`), and `run.done()`, rather than the bare text + done it uses today.
  Verify: the helper compiles and the existing four scenarios in "ChatPage with a write waiting" still pass.

- [ ] 3.2 Add a new scenario: after clicking Approve the `monitorPane` aside (or `data-testid="monitor-panel"`)
  is still present in the DOM. Verify: the new test passes (`cd web && npm test -- --run src/chat/ChatPage.confirmation.test.tsx`).

- [ ] 3.3 Add a new scenario: after the resume stream ends the tool call card from the answer run is visible
  in the conversation. Verify: the new test passes.

## 4. Regression pass

- [ ] 4.1 Run the full web test suite and confirm all tests pass: `make test-web`.

- [ ] 4.2 Run `make lint-web` and confirm no lint or type errors are introduced.
