import { EventType } from '@ag-ui/core';
import { useEffect, useReducer } from 'react';
import { authHeaders } from '../api/client';
import type { RunSummary } from '../api/types';
import { useAuth } from '../auth/useAuth';
import { SseParser } from '../chat/sseParser';

/** The custom events a run's AG-UI stream adds; their names match the server's. */
export const ATTEMPT_EVENT = 'maf-lab/testgen-attempt';
export const DROPPED_EVENT = 'maf-lab/testgen-activity-dropped';

export interface AttemptResult {
  attempt: number;
  before: number | null;
  after: number | null;
  build: string;
  tests: { passed: number; failed: number; skipped: number };
  errors: string[];
  violations: number;
}

/** One line of a run's timeline, as the AG-UI stream told it. */
export type TimelineItem =
  | { kind: 'step'; id: string; name: string }
  | { kind: 'tool'; id: string; name: string; path: string | null; outcome: string | null; summary: string | null }
  | { kind: 'text' | 'reasoning'; id: string; text: string; done: boolean }
  | ({ kind: 'attempt'; id: string } & AttemptResult)
  | { kind: 'notice'; id: string; text: string };

export type RunEnd = { outcome: 'finished' } | { outcome: 'error'; code: string; message: string };

export interface RunStreamState {
  summary: RunSummary | null;
  timeline: TimelineItem[];
  ended: RunEnd | null;
}

export const initialRunStream: RunStreamState = { summary: null, timeline: [], ended: null };

type AguiEvent = { type: string } & Record<string, unknown>;

const parseJson = (text: unknown): Record<string, unknown> => {
  if (typeof text !== 'string') return {};
  try {
    const value: unknown = JSON.parse(text);
    return value && typeof value === 'object' ? (value as Record<string, unknown>) : {};
  } catch {
    return {};
  }
};

const str = (value: unknown): string | null => (typeof value === 'string' ? value : null);

function update(timeline: TimelineItem[], id: string, change: (item: TimelineItem) => TimelineItem): TimelineItem[] {
  return timeline.map((item) => (item.id === id ? change(item) : item));
}

/**
 * Folds one AG-UI event of a test-generation run into what the screen shows. Pure, so the whole mapping is testable
 * without a stream: the summary is the protocol's state, steps are phases, tool calls are joined by their id, text
 * and reasoning by their message id.
 */
export function reduceRunEvent(state: RunStreamState, event: AguiEvent): RunStreamState {
  const { timeline } = state;
  switch (event.type) {
    case EventType.STATE_SNAPSHOT:
      return { ...state, summary: event.snapshot as RunSummary };
    case EventType.STEP_STARTED:
      return {
        ...state,
        timeline: [...timeline, { kind: 'step', id: `step-${timeline.length}`, name: String(event.stepName) }],
      };
    case EventType.TOOL_CALL_START:
      return {
        ...state,
        timeline: [
          ...timeline,
          { kind: 'tool', id: String(event.toolCallId), name: String(event.toolCallName), path: null, outcome: null, summary: null },
        ],
      };
    case EventType.TOOL_CALL_ARGS:
      return {
        ...state,
        timeline: update(timeline, String(event.toolCallId), (item) =>
          item.kind === 'tool' ? { ...item, path: str(parseJson(event.delta).path) } : item,
        ),
      };
    case EventType.TOOL_CALL_RESULT: {
      const result = parseJson(event.content);
      return {
        ...state,
        timeline: update(timeline, String(event.toolCallId), (item) =>
          item.kind === 'tool' ? { ...item, outcome: str(result.outcome), summary: str(result.summary) } : item,
        ),
      };
    }
    case EventType.TEXT_MESSAGE_START:
    case EventType.REASONING_MESSAGE_START:
      return {
        ...state,
        timeline: [
          ...timeline,
          {
            kind: event.type === EventType.TEXT_MESSAGE_START ? 'text' : 'reasoning',
            id: String(event.messageId),
            text: '',
            done: false,
          },
        ],
      };
    case EventType.TEXT_MESSAGE_CONTENT:
    case EventType.REASONING_MESSAGE_CONTENT:
      return {
        ...state,
        timeline: update(timeline, String(event.messageId), (item) =>
          item.kind === 'text' || item.kind === 'reasoning' ? { ...item, text: item.text + String(event.delta ?? '') } : item,
        ),
      };
    case EventType.TEXT_MESSAGE_END:
    case EventType.REASONING_MESSAGE_END:
      return {
        ...state,
        timeline: update(timeline, String(event.messageId), (item) =>
          item.kind === 'text' || item.kind === 'reasoning' ? { ...item, done: true } : item,
        ),
      };
    case EventType.CUSTOM: {
      if (event.name === ATTEMPT_EVENT) {
        const value = event.value as AttemptResult;
        return { ...state, timeline: [...timeline, { kind: 'attempt', id: `attempt-${timeline.length}`, ...value }] };
      }
      if (event.name === DROPPED_EVENT) {
        const text = 'The oldest activity of this run was dropped to keep it within its cap.';
        return { ...state, timeline: [{ kind: 'notice', id: 'dropped', text }, ...timeline] };
      }
      return state;
    }
    case EventType.RUN_FINISHED:
      return {
        ...state,
        summary: (event.result as RunSummary | undefined) ?? state.summary,
        ended: { outcome: 'finished' },
      };
    case EventType.RUN_ERROR:
      return {
        ...state,
        ended: { outcome: 'error', code: String(event.code ?? 'error'), message: String(event.message ?? '') },
      };
    default:
      return state;
  }
}

type Action = { kind: 'reset' } | { kind: 'event'; event: AguiEvent };

const reducer = (state: RunStreamState, action: Action): RunStreamState =>
  action.kind === 'reset' ? initialRunStream : reduceRunEvent(state, action.event);

/** How long to wait before reading a dropped stream again (it replays from the start). */
const RETRY_MS = 2000;

/**
 * A test-generation run as its AG-UI stream tells it: replayed from the start, then live until the run's terminal
 * event. A stream that drops before then is read again from the start. Null `runId` follows nothing.
 */
export function useRunStream(runId: string | null): RunStreamState {
  const { session } = useAuth();
  const token = session?.token ?? null;
  const [state, dispatch] = useReducer(reducer, initialRunStream);

  useEffect(() => {
    dispatch({ kind: 'reset' });
    if (!runId) return;
    const abort = new AbortController();
    void (async () => {
      let ended = false;
      while (!ended && !abort.signal.aborted) {
        try {
          const response = await fetch(`/api/coverage/runs/${encodeURIComponent(runId)}/events`, {
            headers: { Accept: 'text/event-stream', ...authHeaders(token) },
            signal: abort.signal,
          });
          if (!response.ok || !response.body) return;
          dispatch({ kind: 'reset' });
          const reader = response.body.getReader();
          const decoder = new TextDecoder();
          const parser = new SseParser();
          for (;;) {
            const { done, value } = await reader.read();
            const frames = done ? parser.flush() : parser.push(decoder.decode(value, { stream: true }));
            for (const frame of frames) {
              if (!frame.data) continue;
              const event = JSON.parse(frame.data) as AguiEvent;
              dispatch({ kind: 'event', event });
              if (event.type === EventType.RUN_FINISHED || event.type === EventType.RUN_ERROR) ended = true;
            }
            if (done) break;
          }
        } catch {
          // Aborted on unmount, or the connection dropped: read again below unless we were told to stop.
        }
        if (!ended && !abort.signal.aborted) await new Promise((resolve) => setTimeout(resolve, RETRY_MS));
      }
    })();
    return () => abort.abort();
  }, [runId, token]);

  return state;
}
