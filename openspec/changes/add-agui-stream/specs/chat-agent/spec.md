# Spec Delta

## REMOVED Requirements

### Requirement: SSE event stream
**Reason**: The wire between the API and the browser is no longer this capability's
business, and it is no longer a set of names invented here. It is AG-UI.
**Migration**: The same guarantees now live in `agui-stream` — a run begins and ends
exactly once, the answer streams as a text message, a tool call streams as the
protocol's tool events with the start before the tool runs, and the trace and the
sources travel as custom events, all before the run ends. A turn that waits for a
person is an interrupt there rather than an event here.
