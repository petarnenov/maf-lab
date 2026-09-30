# Tasks

## 1. Filter

- [x] 1.1 Add `CitationMarkerChatClient` (`src/Maf.Lab.Api/Agent/`), a `DelegatingChatClient` that removes `【…】` markers and the whitespace before them from streamed and non-streamed text, releases an unclosed marker after 400 characters and at the end of the stream, leaves non-text contents untouched, and reports each removal through an optional callback. Verify: unit tests cover a marker at a sentence end, a marker split across updates, several markers in one update, a stray `【`, text without markers, and reasoning content that passes unchanged

## 2. Wiring

- [x] 2.1 Wrap the turn's `TracingChatClient` with it in `ChatTurnRunner`, count removals on the turn state, and add `citationMarkersRemoved` to `turn.end`. Verify: a chat API test with a fake model that writes markers gets a stream and a stored answer without them, and `turn.end` carries the count
- [x] 2.2 Use it in `AssistantBridge` for A2A replies. Verify: an A2A test with a fake model that writes a marker receives a reply without it
- [x] 2.3 Document `citationMarkersRemoved` in `docs/trace-events.md`. Verify: the field is listed under `turn.end`

## 3. Verification

- [x] 3.1 `make test-dotnet` and `make lint-dotnet` pass (the one full-suite failure, `GuardrailTests.A_hanging_Jev_costs_no_more_than_the_timeouts`, is a timing test that passes 3/3 alone and does not touch this change)
- [x] 3.2 Rebuild the stack, then ask the billing question from the README screenshot three times. Verify: no answer shows a `【`, and the trace shows `citationMarkersRemoved` when the model wrote any
- [x] 3.3 Re-take the chat screenshot with `make screenshots SHOTS=chat`. Verify: the hero image shows no markers
- [x] 3.4 `openspec validate strip-citation-markers --strict` passes
