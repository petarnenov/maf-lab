# Design

## Context

See proposal.md — Why. Jev's guard acts at four sites; the trace must never record what a block or withhold
removed. An audit of the four sites found two conformant and two leaking:

- **User prompt block** — `ChatTurnRunner.RunAsync` fetched tools and emitted the `prompt` event before the
  block short-circuit. Leaking. (The prompt screening already rides in the intent request, so the decision is
  available as soon as the classification returns — earlier than the tool fetch and the `prompt` emit.)
- **Tool-result withhold** — `TraceToolResult` recorded the raw MCP result in the `tool.result` event
  *before* `ScreenToolResultAsync` ran, so a withheld excerpt's content was persisted. Leaking.
- **Reviewer flag** — `ScreenConsultationAsync` runs before the `reviewed` trace event; a flagged verdict
  becomes `Failed(ReviewerNotBelieved)` and the `reviewed` event's `diagnosis` records only our own text.
  Conformant.
- **A2A partner block** — `AssistantBridge.AnswerAsync` returns the fixed refusal before `GetToolsAsync` and
  the prompt build, and its guard trace target is `null` (the partner path writes no turn trace). Conformant.

## Goals / Non-Goals

- Goal: at every guard site, a block/withhold records only the decision and a neutral notice — never the
  blocked prompt, the withheld content, or the downstream work it removed.
- Goal: a benign turn's trace, ordering and content stay byte-for-byte unchanged.
- Non-Goal: changing the guard thresholds, the refusal/notice text, the signals, or anything the UI renders.
- Non-Goal: the passage-relevance gate — it silences low-relevance passages for routing quality, not a
  security withhold, and persists no withheld content, so it is out of scope.

## Decisions

- **Prompt block: move the intent classification and prompt screening above the tool fetch, then gate the
  tool fetch (`await using` a nullable `ToolSet`), the chat-options build, the `prompt` emit and the agent run
  on `!screen.Blocked`.** `forced`/`route` are already false/null for a blocked turn, so the intent and
  `guardrail` events keep their exact shape and order and the benign event is byte-for-byte the same.
  - Alternative — only wrap the `prompt` emit in `if (!screen.Blocked)`: rejected; it leaves the wasted
    `tools/list` round-trip and still builds the tool schemas.
- **Tool-result withhold: screen before tracing, and record the redacted result.** `ScreenToolResultAsync`
  moves ahead of `TraceToolResult`, which now takes the `ScreenedToolResult` and, when anything was withheld,
  records `screened.Structured` (redacted excerpts + notice, or the whole-withheld notice) in place of the raw
  MCP result. Retrieval diagnostics and the serving replica are still read from the raw result, so the
  `retrieval` event is unchanged. When nothing is withheld the raw result is recorded exactly as before, so
  benign turns are unchanged.
  - Alternative — always record `screened.Structured`: rejected; it would change every benign `tool.result`
    event's shape, not just withheld ones.

## Risks / Trade-offs

- [A reorder could change a benign turn's trace] → tool/prompt work runs identically whenever the turn is not
  blocked and nothing is withheld; the existing ordering test and the new regression tests guard it.
- [The persisted trace must reflect both fixes] → both guards sit before `PersistAsync`, which serialises the
  same `trace.Events`, so a blocked prompt is never stored with the system prompt and a withheld excerpt is
  never stored with its content.

## Migration Plan

Behaviour fix to two methods in one class (plus a comment in `Guardrail`); no schema, API or config change.
Rollback is reverting the commit.
