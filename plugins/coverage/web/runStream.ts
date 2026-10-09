import type { AbstractAgent } from '@ag-ui/client';
import { EventType } from '@ag-ui/core';
import { useCopilotKit, type CopilotKitCoreReact } from '@maf/plugin-api';
import { useCallback, useSyncExternalStore } from 'react';
import { agentNamed } from '@maf/plugin-api';
import type { RunSummary } from './types';

/** The test-generation run agent, as CopilotKit's runtime names it. */
export const TESTGEN_AGENT = 'testgen';

/** This page, as one of possibly many following the same run: each follows it on a thread of its own. */
const viewer = crypto.randomUUID().replaceAll('-', '').slice(0, 12);
export const runThread = (runId: string) => `testgen:${runId}:${viewer}`;

/**
 * A run's shared state (agui-protocol-only): its summary, and its record — every attempt finished so far, the stop,
 * each takeover after a restart, and whether the oldest activity was dropped.
 */
export interface RunState extends RunSummary {
  attempts?: AttemptResult[];
  stop?: StopInfo | null;
  resumes?: number[];
  dropped?: boolean;
}

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
  /** How much of the run's record the timeline already shows. */
  shown: { attempts: number; resumes: number; stopped: boolean; dropped: boolean };
}

export const initialRunStream: RunStreamState = {
  summary: null,
  timeline: [],
  ended: null,
  shown: { attempts: 0, resumes: 0, stopped: false, dropped: false },
};

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
    // The run's state: the summary replaces what the screen had, and what the record gained since joins the timeline
    // where it happened — an attempt's result, a takeover, the stop; a dropped record is said first.
    case EventType.STATE_SNAPSHOT:
      return recorded({ ...state, summary: event.snapshot as RunSummary }, event.snapshot as RunState);
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
    case EventType.RUN_FINISHED:
      return { ...state, ended: { outcome: 'finished' } };
    // Why the run failed is in its state, which said so before the end.
    case EventType.RUN_ERROR:
      return {
        ...state,
        ended: {
          outcome: 'error',
          code: state.summary?.reason ?? String(event.code ?? 'error'),
          message: String(event.message ?? ''),
        },
      };
    default:
      return state;
  }
}

/**
 * What the run's record gained since the timeline last showed it, as entries in the order it happened. A state that
 * does not carry part of the record leaves that part as it was.
 */
function recorded(state: RunStreamState, after: RunState): RunStreamState {
  let { timeline } = state;
  const shown = { ...state.shown };
  for (const attempt of (after.attempts ?? []).slice(shown.attempts)) {
    timeline = [...timeline, { kind: 'attempt', id: `attempt-${timeline.length}`, ...attempt }];
  }
  shown.attempts = Math.max(shown.attempts, after.attempts?.length ?? 0);
  for (const attempt of (after.resumes ?? []).slice(shown.resumes)) {
    const text =
      attempt > 0
        ? `The agent restarted and resumed the run at attempt ${attempt}.`
        : 'The agent restarted and resumed the run at the baseline.';
    timeline = [...timeline, { kind: 'notice', id: `resumed-${timeline.length}`, text }];
  }
  shown.resumes = Math.max(shown.resumes, after.resumes?.length ?? 0);
  if (after.stop && !shown.stopped) {
    timeline = [...timeline, { kind: 'stopped', id: 'stopped', ...after.stop }];
    shown.stopped = true;
  }
  if (after.dropped && !shown.dropped) {
    const text = 'The oldest activity of this run was dropped to keep it within its cap.';
    timeline = [{ kind: 'notice', id: 'dropped', text }, ...timeline];
    shown.dropped = true;
  }
  return { ...state, timeline, shown };
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
  stop: () => void;
}

const streams = new Map<string, SharedStream>();

const streamKey = (runId: string) => runId;

/** How many run streams are open now; for tests. */
export const openRunStreams = () => streams.size;

/**
 * Follows one run through CopilotKit (agui-protocol-only): a run of the test-generation agent on the run's thread,
 * which replays the run from the start and then follows it live. A stream that drops before the run's end is run again.
 */
function openStream(runId: string, copilotkit: CopilotKitCoreReact): SharedStream {
  let stopped = false;
  let agent: AbstractAgent | null = null;
  const shared: SharedStream = {
    state: initialRunStream,
    listeners: new Set(),
    // Nobody follows the run any more: CopilotKit's runtime stops reading it for this page. The run itself goes on.
    stop: () => {
      stopped = true;
      if (agent?.isRunning) copilotkit.stopAgent({ agent });
    },
  };
  const set = (next: RunStreamState) => {
    shared.state = next;
    shared.listeners.forEach((listener) => listener());
  };
  void (async () => {
    let ended = false;
    while (!ended && !stopped) {
      const base = await agentNamed(copilotkit, TESTGEN_AGENT);
      if (base) {
        const following = base.clone();
        agent = following;
        following.threadId = runThread(runId);
        following.setMessages([]);
        // The run replays from the start, so what an earlier connection showed is replaced.
        let state = initialRunStream;
        set(state);
        const subscription = following.subscribe({
          onEvent: ({ event }: { event: { type: string } }) => {
            state = reduceRunEvent(state, event as AguiEvent);
            if (event.type === EventType.RUN_FINISHED || event.type === EventType.RUN_ERROR) ended = true;
            if (!stopped) set(state);
          },
        });
        try {
          await copilotkit.runAgent({ agent: following });
        } catch {
          // Stopped by the last subscriber leaving, or the connection dropped: run again below unless told to stop.
        } finally {
          subscription.unsubscribe();
        }
      }
      if (!ended && !stopped) await new Promise((resolve) => setTimeout(resolve, RETRY_MS));
    }
  })();
  return shared;
}

function subscribe(runId: string, copilotkit: CopilotKitCoreReact, listener: () => void): () => void {
  const key = streamKey(runId);
  let shared = streams.get(key);
  if (!shared) {
    shared = openStream(runId, copilotkit);
    streams.set(key, shared);
  }
  shared.listeners.add(listener);
  const mine = shared;
  return () => {
    mine.listeners.delete(listener);
    if (mine.listeners.size === 0 && streams.get(key) === mine) {
      streams.delete(key);
      mine.stop();
    }
  };
}

/**
 * A test-generation run as its AG-UI agent tells it, through CopilotKit: replayed from the start, then live until the
 * run's terminal event. A stream that drops before then is read again from the start. Null `runId` follows nothing. Every caller for
 * the same run shares one stream.
 */
export function useRunStream(runId: string | null): RunStreamState {
  const { copilotkit } = useCopilotKit();
  const subscribeTo = useCallback(
    (listener: () => void) => (runId ? subscribe(runId, copilotkit, listener) : () => {}),
    [runId, copilotkit],
  );
  const snapshot = useCallback(
    () => (runId ? (streams.get(streamKey(runId))?.state ?? initialRunStream) : initialRunStream),
    [runId],
  );
  return useSyncExternalStore(subscribeTo, snapshot, snapshot);
}
