# Proposal

## Why

Jev's content guard (§35) refuses or withholds at four sites, and the principle should hold at every one:
when the guard stops something, nothing downstream is built, sent to the model, or recorded in the trace —
the trace holds only the decision and a neutral notice, never the blocked prompt, the withheld content, or
the work that would have followed. Two sites broke it:

- **A blocked user prompt** still had `ChatTurnRunner` fetch the tools over MCP and emit the `prompt` trace
  event with the full system prompt and every tool schema — the very thing a "show me your system prompt"
  injection was trying to extract — then stream and persist it.
- **A withheld tool result** was recorded raw in the `tool.result` trace event *before* screening, so a
  withheld excerpt's content (e.g. a poisoned "ignore previous instructions" snippet) was persisted in the
  trace even though it never reached the model or the sources.

The other two sites already behaved: a flagged reviewer's words become the neutral "not believed" outcome
before anything is traced, and a blocked A2A partner prompt returns the fixed refusal before any tool fetch
or prompt build (and writes no turn trace at all). These are not cross-tenant leaks — the trace is owner and
same-firm-admin only, and the system prompt holds no secrets — but they are a defense-in-depth gap and an
internal inconsistency: the guard withholds from the model and the trace then publishes anyway.

## What Changes

- On a guardrail-blocked prompt, `ChatTurnRunner` no longer fetches tools, builds the prompt/tool chat
  options, or emits the `prompt` trace event. A blocked turn's trace holds only turn start, intent, the
  `guardrail` (blocked) event, the refusal `answer.delta`(s), sources, signals and turn end.
- A tool result is screened *before* it is traced, and a withheld item's content is redacted from the
  `tool.result` event: the recorded result is the redacted one the model may read (the neutral notice and a
  count), never the withheld excerpt or record.
- A general requirement captures the principle across all four guard sites (prompt block, tool-result
  withhold, reviewer flag, A2A partner block), with a scenario each; the reviewer and partner sites are
  already conformant.
- No change to a benign turn's trace, the guard thresholds or behaviour, the refusal/notice text, or the
  `guardrail_blocked` / `guardrail_withheld` signals.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `turn-tracing`: the "Guard decisions are traced" requirement is generalised — when the guard blocks or
  withholds, the turn does not perform or record the downstream step for the blocked/withheld item, and the
  blocked prompt / withheld content is never in the trace. Scenarios are added for each of the four sites.

## Impact

- `src/Maf.Lab.Api/Agent/ChatTurnRunner.cs` — `RunAsync` reordered so the prompt-screen decision precedes
  the tool fetch and prompt emit (both guarded behind `!screen.Blocked`); the tool-result path screens before
  it traces, and `TraceToolResult` records the redacted result when anything was withheld.
- `src/Maf.Lab.Api/Agent/Guardrail.cs` — comment only (the trace no longer holds a withheld item's text).
- `tests/Maf.Lab.Tests/` — regression tests: a blocked turn's trace (live and persisted) omits `prompt`
  (and tools/history/envelope); a withheld excerpt's content is absent from the `tool.result` trace.
- No API, DB schema, dependency or configuration change.
