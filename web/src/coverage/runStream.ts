import { EventType } from '@ag-ui/core';
import { useCallback, useSyncExternalStore } from 'react';
import { authHeaders } from '../api/client';
import type { RunSummary } from '../api/types';
import { useAuth } from '../auth/useAuth';
import { SseParser } from '../chat/sseParser';

/** The custom events a run's AG-UI stream adds; their names match the server's. */
export const ATTEMPT_EVENT = 'maf-lab/testgen-attempt';
export const DROPPED_EVENT = 'maf-lab/testgen-activity-dropped';
export const STOPPED_EVENT = 'maf-lab/testgen-stopped';
export const RESUMED_EVENT = 'maf-lab/testgen-resumed';

export interface StopInfo {
  reason: string;
  lastAttempt: number;
  bestPct?: number | null;
  notStarted?: number | null;
}

/**
 * What one runner job of an attempt ran: its scope, the related test files, the tests that ran, why the whole suite ran
 * instead, and whether the runner reused a result it had already computed. `pct` is set on a confirmation.
 */
export interface AttemptRun {
  scope: string;
  files: number;
  tests: number;
  reason?: string | null;
  reused?: boolean;
  pct?: number | null;
}

export interface AttemptResult {
  attempt: number;
  before: number | null;
  after: number | null;
  build: string;
  tests: { passed: number; failed: number; skipped: number };
  errors: string[];
  violations: number;
  /** What the attempt's measured run ran; absent on entries recorded before attempts said so. */
  run?: AttemptRun | null;
  /** The whole-suite run that confirmed the attempt, whose counts are then the attempt's. */
  confirmation?: AttemptRun | null;
}

const count = (n: number, one: string, many: string) => `${n.toLocaleString()} ${n === 1 ? one : many}`;

/**
 * What an attempt ran, in words: "related: 3 files, 58 tests → whole suite: 1,219 tests (confirmation)", or
 * "whole suite: <why>" when the whole suite ran instead. Null for an entry that does not say.
 */
export function attemptScope({ run, confirmation }: Pick<AttemptResult, 'run' | 'confirmation'>): string | null {
  if (!run) return null;
  const reused = run.reused ? ' (reused)' : '';
  const ran =
    run.scope === 'related'
      ? `related: ${count(run.files, 'file', 'files')}, ${count(run.tests, 'test', 'tests')}${reused}`
      : `whole suite: ${run.reason ? run.reason : count(run.tests, 'test', 'tests')}${reused}`;
  if (!confirmation) return ran;
  const how = confirmation.reused ? 'confirmation, reused' : 'confirmation';
  return `${ran} → whole suite: ${count(confirmation.tests, 'test', 'tests')} (${how})`;
}

/** One line of a run's timeline, as the AG-UI stream told it. */
export type TimelineItem =
  | { kind: 'step'; id: string; name: string }
  | { kind: 'tool'; id: string; name: string; path: string | null; outcome: string | null; summary: string | null }
  | { kind: 'text' | 'reasoning'; id: string; text: string; done: boolean }
  | ({ kind: 'attempt'; id: string } & AttemptResult)
  | { kind: 'notice'; id: string; text: string }
  | ({ kind: 'stopped'; id: string } & StopInfo);

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
      if (event.name === STOPPED_EVENT) {
        return { ...state, timeline: [...timeline, { kind: 'stopped', id: 'stopped', ...(event.value as StopInfo) }] };
      }
      if (event.name === RESUMED_EVENT) {
        const attempt = Number((event.value as { attempt?: number } | undefined)?.attempt ?? 0);
        const text =
          attempt > 0
            ? `The agent restarted and resumed the run at attempt ${attempt}.`
            : 'The agent restarted and resumed the run at the baseline.';
        return { ...state, timeline: [...timeline, { kind: 'notice', id: `resumed-${timeline.length}`, text }] };
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

/** How long to wait before reading a dropped stream again (it replays from the start). */
const RETRY_MS = 2000;

/**
 * One stream per run, shared by everyone on the page who follows it (the file's status, the Activity modal, the
 * tree row): the browser holds only a few connections to the server at a time, so they must not each open their own.
 * The stream opens with the first subscriber, keeps its state for a late one, and closes with the last.
 */
interface SharedStream {
  state: RunStreamState;
  listeners: Set<() => void>;
  abort: AbortController;
}

const streams = new Map<string, SharedStream>();

const streamKey = (runId: string, token: string | null) => `${runId}\u0000${token ?? ''}`;

/** How many run streams are open now; for tests. */
export const openRunStreams = () => streams.size;

function openStream(runId: string, token: string | null): SharedStream {
  const shared: SharedStream = { state: initialRunStream, listeners: new Set(), abort: new AbortController() };
  const set = (next: RunStreamState) => {
    shared.state = next;
    shared.listeners.forEach((listener) => listener());
  };
  const { signal } = shared.abort;
  void (async () => {
    let ended = false;
    while (!ended && !signal.aborted) {
      try {
        const response = await fetch(`/api/coverage/runs/${encodeURIComponent(runId)}/events`, {
          headers: { Accept: 'text/event-stream', ...authHeaders(token) },
          signal,
        });
        if (!response.ok || !response.body) return;
        // The stream replays the run from the start, so what an earlier connection showed is replaced.
        let state = initialRunStream;
        set(state);
        const reader = response.body.getReader();
        const decoder = new TextDecoder();
        const parser = new SseParser();
        for (;;) {
          const { done, value } = await reader.read();
          const frames = done ? parser.flush() : parser.push(decoder.decode(value, { stream: true }));
          for (const frame of frames) {
            if (!frame.data) continue;
            const event = JSON.parse(frame.data) as AguiEvent;
            state = reduceRunEvent(state, event);
            if (event.type === EventType.RUN_FINISHED || event.type === EventType.RUN_ERROR) ended = true;
          }
          if (frames.length > 0 && !signal.aborted) set(state);
          if (done) break;
        }
      } catch {
        // Aborted by the last subscriber leaving, or the connection dropped: read again below unless told to stop.
      }
      if (!ended && !signal.aborted) await new Promise((resolve) => setTimeout(resolve, RETRY_MS));
    }
  })();
  return shared;
}

function subscribe(runId: string, token: string | null, listener: () => void): () => void {
  const key = streamKey(runId, token);
  let shared = streams.get(key);
  if (!shared) {
    shared = openStream(runId, token);
    streams.set(key, shared);
  }
  shared.listeners.add(listener);
  const mine = shared;
  return () => {
    mine.listeners.delete(listener);
    if (mine.listeners.size === 0 && streams.get(key) === mine) {
      streams.delete(key);
      mine.abort.abort();
    }
  };
}

/**
 * A test-generation run as its AG-UI stream tells it: replayed from the start, then live until the run's terminal
 * event. A stream that drops before then is read again from the start. Null `runId` follows nothing. Every caller for
 * the same run shares one stream.
 */
export function useRunStream(runId: string | null): RunStreamState {
  const { session } = useAuth();
  const token = session?.token ?? null;
  const subscribeTo = useCallback(
    (listener: () => void) => (runId ? subscribe(runId, token, listener) : () => {}),
    [runId, token],
  );
  const snapshot = useCallback(
    () => (runId ? (streams.get(streamKey(runId, token))?.state ?? initialRunStream) : initialRunStream),
    [runId, token],
  );
  return useSyncExternalStore(subscribeTo, snapshot, snapshot);
}
