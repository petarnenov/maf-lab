# Tasks

## 1. Server

- [x] 1.1 Add `ConversationRow.FocusAccountId` (nullable) and `ConversationDetail.Focus`, and pass `RunAgentInput.State` from `ChatEndpoints` to the runner. Verify the build, and with a history test that an older conversation returns `focus: null`.
- [x] 1.2 In `ChatTurnRunner`:
  - resolve the starting focus (client value validated against the offered set, else stored) and trace it (design §2, §5);
  - append the focus note to `Instructions`;
  - emit `STATE_SNAPSHOT` after `RUN_STARTED`;
  - move the focus after a successful single-account holdings or AUM read and emit a new snapshot after its card;
  - persist it.

  Verify with api host tests:
  - starting snapshot from the stored focus;
  - a read moves the focus and emits a snapshot;
  - `list_my_accounts` does not move it;
  - a client id that was offered is accepted;
  - one never offered is rejected and traced without the id;
  - `focus: null` clears;
  - the prompt note appears only with a focus.
- [x] 1.3 Add the focus fallback to `DataToolRouter` through `IIntentClassifier.ClassifyAsync(..., focusAccountId)`. Verify with `DataToolRoutingTests`:
  - an account-less portfolio question with a focus routes with it;
  - a named id wins;
  - no focus still gives up;
  - `list_my_accounts` is unchanged;
  - the Jev request body is identical with and without a focus.

## 2. Web

- [x] 2.1 Map `EventType.STATE_SNAPSHOT` in `chatEvents`, keep `focus` in the reducer (plus a `setFocus` action and hydrate from `ConversationDetail.focus`), and send `state: { focus }` with every request in `useChatStream`. Verify with reducer, chatEvents and useChatStream tests.
- [x] 2.2 Add `FocusChip` above the composer (✕ clears, language from the last question) and "Focus" / "Фокус" buttons on the accounts rows and on the holdings and AUM cards. Verify with `ChatPage` tests:
  - the chip appears from a snapshot;
  - pressing "Focus" on an accounts row then sending carries the state;
  - ✕ then sending carries `focus: null`;
  - a different server snapshot replaces the chip.

## 3. Verification

- [x] 3.1 Run `make test`, `make lint` and `make build-web`; all green (web serially if the machine's parallel workers crash again).
- [x] 3.2 Rebuild with `make`. At http://localhost:7171/chat (firm-a):
  - "Препоръчай ребалансиране за A-1043", then "а AUM-ът по тримесечия?": the AUM read is for A-1043 and the chip shows A-1043;
  - "Which accounts do I have?", then "Focus" on A-1044, then "What does it hold?": A-1044 is read;
  - ✕, then "What does it hold?": no account is assumed; the model asks which one (the cleared-focus note, added after the first live check assumed A-1044 from history).

  Then run `make eval SUITE=selection` and `SUITE=presentation` against their baselines.
- [x] 3.3 Add a DECISIONS entry (server-owned focus, offered-set validation, prompt note, routing fallback and why no Jev change), document the state in `docs/http-api.md` and the `focus` trace kind in `docs/trace-events.md`, then run `openspec validate add-focus-state --strict`; valid.
