import type { SourceRef, TraceEvent } from '../api/types';
import type { ToolCallView } from '../chat/chatReducer';
import {
  byKind,
  dataOf,
  type AuditData,
  type ToolCallData,
  type ToolResultData,
} from './traceData';

export interface ReconstructedTurn {
  /** Answer text as it had streamed by the cursor. */
  text: string;
  /** What the model had reasoned by the cursor. */
  reasoning: string;
  /** How long it spent reasoning over the whole turn, as the trace timed it. */
  reasoningMs?: number;
  toolCalls: ToolCallView[];
  /** Empty until the `sources` step is reached. */
  sources: SourceRef[];
  stepLabel: string;
  /** False for traces recorded before answer text was traced; `text` is then the final answer. */
  textRecorded: boolean;
}

export interface FinalTurn {
  text: string;
  sources: SourceRef[];
}

interface AnswerDelta {
  offset?: number;
  text?: string;
}

const FREE_TEXT = new Set(['query', 'question', 'text', 'message', 'comment', 'note']);

/** The chat view of a turn as it was after the first `cursor` trace events. */
export function reconstructTurn(
  events: TraceEvent[],
  cursor: number,
  final: FinalTurn = { text: '', sources: [] },
): ReconstructedTurn {
  const count = events.length;
  const at = Math.min(Math.max(cursor, 0), count);
  const visible = events.slice(0, at);
  const textRecorded = events.some((e) => e.kind === 'answer.delta');

  const thinking = reasoningOf(events, at);
  const text = textRecorded
    ? byKind(visible, 'answer.delta')
        .map((e) => dataOf<AnswerDelta>(e))
        .sort((a, b) => (a.offset ?? 0) - (b.offset ?? 0))
        .map((d) => d.text ?? '')
        .join('')
    : final.text;

  // Identifier-only argument summaries come from the audit rows (same as the live cards).
  const auditArgs = new Map<string, string>();
  for (const e of byKind(events, 'audit')) {
    const d = dataOf<AuditData & { callId?: string }>(e);
    if (d.callId && d.arguments !== undefined) auditArgs.set(d.callId, d.arguments);
  }
  const results = new Map<string, ToolResultData>();
  for (const e of byKind(visible, 'tool.result')) {
    const d = dataOf<ToolResultData>(e);
    if (d.callId) results.set(d.callId, d);
  }

  const toolCalls: ToolCallView[] = [];
  for (const e of visible) {
    if (e.kind === 'tool.call') {
      const d = dataOf<ToolCallData>(e);
      const callId = d.callId ?? `seq-${e.seq}`;
      const result = results.get(callId);
      const summary = result ? summarise(d.tool ?? '', result) : undefined;
      toolCalls.push({
        callId,
        toolName: d.tool ?? 'tool',
        argumentSummary: auditArgs.get(callId) ?? argumentSummary(d.arguments),
        status: result ? 'finished' : 'running',
        resultSummary: summary?.text,
        sourceCount: summary?.sources,
        isError: result?.isError ?? false,
      });
    } else if (e.kind === 'tool.unknown') {
      const d = dataOf<ToolCallData>(e);
      toolCalls.push({
        callId: d.callId ?? `seq-${e.seq}`,
        toolName: d.tool ?? 'tool',
        argumentSummary: '',
        status: 'finished',
        resultSummary: 'tool does not exist',
        sourceCount: 0,
        isError: true,
      });
    }
  }

  const sourcesEvent = byKind(visible, 'sources')[0];
  const sources = sourcesEvent
    ? (
        dataOf<{ sources?: { docId: string; sectionPath: string }[] }>(sourcesEvent).sources ?? []
      ).map(
        (s) =>
          final.sources.find((f) => f.docId === s.docId && f.sectionPath === s.sectionPath) ?? {
            docId: s.docId,
            sectionPath: s.sectionPath,
            sourcePath: s.docId,
            snippet: '',
          },
      )
    : [];

  return {
    text,
    reasoning: thinking.text,
    reasoningMs: thinking.ms,
    toolCalls,
    sources,
    stepLabel: `step ${at} of ${count}`,
    textRecorded,
  };
}

/**
 * The model's reasoning as of a step, out of a stored trace. `ms` is the span of the recorded chunks, which is how
 * long the turn was thinking; a live turn measures it as it happens instead.
 */
export function reasoningOf(
  events: TraceEvent[],
  cursor = events.length,
): { text: string; ms?: number } {
  const all = byKind(events, 'reasoning.delta');
  if (all.length === 0) return { text: '' };
  const at = Math.min(Math.max(cursor, 0), events.length);
  const text = byKind(events.slice(0, at), 'reasoning.delta')
    .map((e) => dataOf<AnswerDelta>(e))
    .sort((a, b) => (a.offset ?? 0) - (b.offset ?? 0))
    .map((d) => d.text ?? '')
    .join('');
  return { text, ms: all[all.length - 1].atMs - all[0].atMs };
}

/** Same result summaries the API sends in tool_call_finished. */
function summarise(tool: string, result: ToolResultData): { text: string; sources: number } {
  if (result.isError) return { text: 'error', sources: 0 };
  const structured = (result.result as { structuredContent?: Record<string, unknown> } | null)
    ?.structuredContent;
  if (!structured) return { text: 'done', sources: 0 };
  switch (tool) {
    case 'search_documents': {
      const n = Array.isArray(structured.results) ? structured.results.length : 0;
      return { text: n === 0 ? 'no matching documentation' : `${n} snippet(s)`, sources: n };
    }
    case 'get_billing_run_status':
      return {
        text: `run ${String(structured.runId ?? '')}: ${String(structured.status ?? '')}`,
        sources: 0,
      };
    case 'search_billing_runs':
      return {
        text: `${Array.isArray(structured.runs) ? structured.runs.length : 0} run(s)`,
        sources: 0,
      };
    default:
      return { text: 'done', sources: 0 };
  }
}

/** Fallback when no audit row exists yet: identifier arguments only, never free text. */
function argumentSummary(args: unknown): string {
  if (!args || typeof args !== 'object') return '';
  return Object.entries(args as Record<string, unknown>)
    .filter(
      ([key, value]) => !FREE_TEXT.has(key.toLowerCase()) && value !== null && value !== undefined,
    )
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([key, value]) => `${key}=${Array.isArray(value) ? value.join(',') : String(value)}`)
    .join(' ');
}
