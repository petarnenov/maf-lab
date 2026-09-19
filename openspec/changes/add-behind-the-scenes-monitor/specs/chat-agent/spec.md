# Spec Delta

## MODIFIED Requirements

### Requirement: SSE event stream
The chat endpoint SHALL stream a turn as server-sent events of types
`text_delta`, `tool_call_started` (tool name and argument summary),
`tool_call_finished` (result summary and source count), `sources` (docId,
sectionPath, snippet), `trace` (one turn-trace event: sequence number, elapsed
milliseconds, kind, title, optional duration and structured data), and `done`.
`tool_call_started` SHALL be emitted before the tool executes and `sources`
before `done`. `trace` events MAY interleave with every other event and MUST
all arrive before `done`.

#### Scenario: Event ordering
- **WHEN** a turn calls `search_documents`
- **THEN** the client receives `tool_call_started` before the tool executes, then `tool_call_finished`, then `sources`, and `done` last

#### Scenario: Trace events in the stream
- **WHEN** any turn runs
- **THEN** the stream contains `trace` events with strictly increasing sequence numbers, the first of kind `turn.start` and the last before `done` of kind `turn.end`
