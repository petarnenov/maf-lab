# Proposal

## Why

maf-lab exists to learn how retrieval-as-a-tool works end to end, but a chat turn is a black box to the user today.
They see the answer, tool cards and sources. They do not see what the agent actually did: the intent decision and
forced retrieval, the history window, the exact messages and tool schemas sent to the model, each model call's tokens
and latency, the raw MCP traffic, how hybrid search ranked candidates (dense vs sparse vs fused, rerank), which
replicas served the turn, or which signals were raised. A live "behind the scenes" view turns every question into a
lesson and makes debugging the agent, the prompt and retrieval immediate.

## What Changes

- **Two-pane chat:** the conversation moves to the left, and a new **behind-the-scenes monitor** fills the right side.
  On narrow screens the panes stack.
- **Full turn trace:** the agent host records a structured trace of every turn, streamed live to the monitor while the
  turn runs. It covers:
  - the principal and the serving api replica;
  - the intent and forced-retrieval decision;
  - the history window (messages and token counts);
  - the system prompt version and text, and the tools offered with their schemas;
  - every model call (full request messages and options, response text, tool calls, finish reason, token usage,
    latency, model and endpoint);
  - emulated forced calls;
  - every tool call (full arguments, raw MCP result, error flag, latency, serving mcp replica);
  - the data envelope handed to the model;
  - audit rows, sources, signals and memory writes;
  - the end-to-end timeline.
- **Retrieval internals from the MCP server:**
  - When the agent asks for it, `search_documents` returns diagnostics in the MCP result `_meta`: tenant scope,
    settings, BM25 query terms and IDF weights, per-branch candidates, rerank order, and embed and Qdrant timings.
  - The model never sees `_meta`.
- **Persistence:**
  - Traces are stored per turn in the content database and kept for 7 days.
  - Clicking any past assistant turn shows its trace.
  - A FIRM_ADMIN reviewing a flagged turn of their firm can open it from the review queue.
- **Scope:** every user sees full traces of their own turns only, and admins of the same firm see traces in the review
  queue. Traces are not logs: logs still carry no message content.
- **BREAKING (SSE, additive):** the chat stream gains a `trace` event type. Existing events and their order are
  unchanged.

## Capabilities

### New Capabilities
- `turn-tracing`: what a turn trace contains, how it is captured, streamed, capped, persisted, retained and scoped.

### Modified Capabilities
- `chat-agent`: the SSE event stream adds `trace` events (Requirement "SSE event stream").
- `web-ui`: the chat screen becomes two panes with the monitor on the right (new requirements; "Streaming chat with
  visible tool use" keeps its behaviour in the left pane).
- `retrieval-tool`: `search_documents` can return retrieval diagnostics in `_meta` on request (new requirement).

## Impact

- API: a trace collector, a tracing chat client, tool-middleware hooks, the `TurnTraces` table with a retention job,
  `GET /api/turns/{turnId}/trace`, and the new SSE event.
- MCP server: builds a diagnostics block when the request `_meta` asks for it. The tool output contract for the model
  is unchanged.
- Web: two-pane layout, monitor components (timeline, model calls, retrieval, MCP, prompt and memory), trace reducer,
  and tests.
- No new packages.
