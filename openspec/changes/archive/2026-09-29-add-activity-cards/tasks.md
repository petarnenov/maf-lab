# Tasks

## 1. Server: emit the card

- [x] 1.1 Add `AGUIStream.Cards`, the allow-list from design §2, and `AGUIStream.Card(callId, activityType, content)`. Add a test that every allow-listed tool's output schema has only permitted property kinds and names. Verify the test passes, and that it fails when a free `string Note` is added to one of the DTOs locally.
- [x] 1.2 In `ChatTurnRunner`'s tool middleware, queue a card for an allow-listed, successful, structured result that the guardrail did not withhold. Write it to the channel after that call's `TOOL_CALL_RESULT`, add a `card` trace event, and keep the cards on the turn state. Verify with api host tests (`ApiFactory`, a fake portfolio tool source):
  - one `ACTIVITY_SNAPSHOT` of type `maf-lab/holdings` after the result;
  - none for `search_documents`;
  - none for an error or a withheld result;
  - the `TOOL_CALL_RESULT` is still a summary;
  - no free text in the card.
- [x] 1.3 Add `TurnRow.ActivitiesJson` (default `"[]"`), written at turn end, and `HistoryTurn.Activities` in the conversation detail. Verify with tests that a reopened conversation returns the card, and that a row stored before the column exists returns `[]`.

## 2. Web: render the card

- [x] 2.1 Map `EventType.ACTIVITY_SNAPSHOT` in `chatEvents.ts` and add `cards` to `AssistantTurn`, with replace-by-`messageId`, `hydrate` from history and `reconstructTurn` from `card` trace events. Verify with reducer and chatEvents tests, including an unknown type being ignored.
- [x] 2.2 Add `web/src/chat/cards/`: `format.ts` (locale from the question, currency, percent), `toCsv`, `CardView`, `HoldingsCard` (drift bar, trade words, badge, total row), `AumHistoryCard` and `AccountsCard`, with a CSS module. Verify with Vitest:
  - Bulgarian formatting "268 000 $" / "20,6 %";
  - English formatting;
  - Buy/Sell words;
  - an outside-tolerance marker by text;
  - CSV output;
  - table semantics (caption, `th scope`).
- [x] 2.3 Render `CardView` for each card in `ChatPage` between the tool-call cards and the text, respecting time travel. Verify with `ChatPage` tests:
  - the card is shown before any answer text arrives;
  - a restored conversation shows it;
  - replay to a step before the tool hides it.

## 3. Verification

- [x] 3.1 Run `make test`, `make lint` and `make build-web`; all green.
- [x] 3.2 Rebuild with `make`. At http://localhost:7171/chat, ask "Препоръчай ребалансиране за A-1043" (firm-a) and "Show the quarter-end AUM of A-1043". Verify:
  - the cards appear before the text, formatted in each language;
  - they survive a reload of the conversation;
  - they show at the right step in time travel;
  - at phone width they scroll inside the card.
- [x] 3.3 Add a DECISIONS entry (the allow-list rule, why the runner emits the card, the stored column), and document the card types in `docs/http-api.md`. Then run `openspec validate add-activity-cards --strict`; valid.
