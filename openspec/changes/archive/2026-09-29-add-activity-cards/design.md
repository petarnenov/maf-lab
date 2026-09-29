# Design

## Context

- **Where the result is available.** `ChatTurnRunner`'s tool middleware unpacks each result
  (`ToolDataEnvelope.Unpack`) and screens it (`guardrail.ScreenToolResultAsync`, which may withhold). It then
  summarises it for the audit and the redacted `TOOL_CALL_RESULT`. The runner also writes its own events, such as the
  `maf-lab/sources` custom event, into the same `ChannelWriter<BaseEvent>` that the adapter's events go through.
- **Redaction.** `RunRedaction` rewrites the adapter's `TOOL_CALL_RESULT` to a summary. Events the runner writes
  itself do not pass through it.
- **Protocol types.** `AGUI.Abstractions` 1.0.0 has `ActivitySnapshotEvent` (`MessageId`, `ActivityType`, `Content`,
  `Replace`, `SubagentRunId`), and `@ag-ui/core` 1.0.0 has `EventType.ACTIVITY_SNAPSHOT`.
- **Rejoin.** `RunFrameRecorder` records every frame written for a run, so a rejoin (`agui-stream` "rejoined from any
  replica") replays cards with no extra work.
- **Storage.** Turns are stored in `TurnRow` (`ToolCallsJson`, `SourcesJson`). `DatabaseInitializer` adds a column the
  model maps but the table lacks. `HistoryTurn` is what `/api/conversations/{id}` returns.
- **Web.** `chatEvents.ts` translates frames into reducer actions. `AssistantTurn` holds `toolCalls` and `sources`.
  Time travel rebuilds a turn from its trace (`reconstructTurn`).

## Goals / Non-Goals

**Goals:**
- A typed, protocol-native card for each portfolio read, shown the moment the data exists and kept with the turn.
- The redaction contract stays a contract: an explicit allow-list, not a loosened rule.

**Non-Goals:**
- No billing cards in this change: the billing run DTOs carry failure detail text and need their own review.
- No `ACTIVITY_DELTA`: each card is final when sent.
- No interactivity beyond copying. There is no sorting or editing, and no frontend tools.
- The model's wording is not changed here (`add-system-prompt-v3` does that).

## Decisions

1. **The runner emits the card, not the adapter.**
   - Where: in the tool middleware after screening, only when `!isError`, the structured result is present, and the
     screening did not withhold it.
   - What: the runner writes `ActivitySnapshotEvent { MessageId = $"card-{callId}", ActivityType, Content = structured }`
     to the channel.
   - Ordering: it is written after the adapter's `TOOL_CALL_RESULT` for that call. The middleware returns the result
     first, so the card is queued and flushed by the runner's event loop once it has passed that call's result event.
   - *Alternative:* an `AGUI.Server` mapping hook. Rejected: §26 found that the hooks add events after the built-in
     ones and see the raw update, and the runner already owns the channel and the order.

2. **The allow-list is code.**
   - `AGUIStream.Cards` is a `FrozenDictionary<string tool, string activityType>` with the three entries.
   - A unit test asserts that each listed tool's `OutputSchemaType` has only number, bool, date, enum, id and name
     properties, with an explicit list of the permitted string properties. A new string field fails the build's tests
     until someone reviews it.

3. **The content is the screened structured result as-is** (camelCase JSON, the same schema the model reads).
   - There is no second view DTO: the tool's DTO is already designed without free text, and a copy would drift.

4. **Tracing, storage and history:**
   - Trace: kind `card`, data `{ callId, activityType, content }`. It holds numbers and names only, like the tool
     events already hold ids.
   - Storage: `TurnRow.ActivitiesJson` (default `"[]"`) is written at turn end from the cards the turn emitted.
   - History: `HistoryTurn.Activities: IReadOnlyList<HistoryActivity(MessageId, ActivityType, JsonElement Content)>`.
   - Retention and deletion follow the row.

5. **Web:**
   - `chatEvents` maps `ACTIVITY_SNAPSHOT` into `{ type: 'card', messageId, activityType, content }`. The reducer
     appends to `AssistantTurn.cards`, replacing a card with the same `messageId`, per the protocol's snapshot rule.
   - `hydrate` maps `HistoryTurn.activities`, and `reconstructTurn` adds cards from `card` trace events up to the step.
   - `web/src/chat/cards/`:
     - `CardView` switches on `activityType`; an unknown type renders `null`.
     - `HoldingsCard`, `AumHistoryCard`, `AccountsCard`, each a `<table>` with `<caption>` and `scope` headers.
     - `format.ts`: `Intl.NumberFormat(locale, { style: 'currency', currency, currencyDisplay: 'narrowSymbol',
       maximumFractionDigits: 0 })` and a percent formatter with 1 dp. The locale is bg-BG when the user turn's text
       has Cyrillic, en-US otherwise, the same rule as the refusal texts.
     - `toCsv(rows)`, copied with `navigator.clipboard.writeText`.
   - Drift bar: inline SVG, following the charts' approach (no chart library, §34). It shows a band at ±tolerance
     around 0, and a marker at the drift. A value outside the band gets a "⚠ извън толеранса" / "⚠ outside tolerance"
     label.
   - Styling: CSS module with the app's existing tokens, `font-variant-numeric: tabular-nums`, and a wrapper with
     `overflow-x: auto`.

## Risks / Trade-offs

- [A wider protocol surface, since data now reaches the client] → Only allow-listed, free-text-free types; the schema
  test (§2); tenancy unchanged, because the tool read is firm-scoped. The trace and stored JSON gain numbers only.
- [The model still restates the table] → The card is additive, so the risk is duplication, not error. Addressed by
  `add-system-prompt-v3`.
- [A stale card in history after the data changes] → A card is a snapshot of that turn's read, labelled with its as-of
  date. That is correct history, not a bug.
