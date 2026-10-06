import type { PluginRunObserver } from '@maf/plugin-api';
import {
  framesFor,
  initialTraceState,
  traceFor,
  traceReducer,
  type TraceAction,
  type TraceState,
} from './traceReducer';
import type { AguiFrame, TraceEvent } from './types';

/** How many runs the monitor holds on to; an older one is read back from its stored trace. */
export const KEPT_RUNS = 50;

/** One run as the monitor follows it: the turn it answers into, and how it ended once it has. */
interface Run {
  turnKey: string;
  startedAt: number;
  seq: number;
  ended?: 'finished' | 'stopped' | 'failed';
}

/**
 * What the monitor knows of this session's runs (an external store, read with React's useSyncExternalStore): each
 * run's AG-UI frames, as the chat's run observer hands over its official events, and its trace events, as the pane
 * reads them from the trace API. Keyed by the chat's turn key, and found by the run id as well.
 */
export function createMonitorStore(now: () => number = () => performance.now()) {
  let state: TraceState = initialTraceState;
  const runs = new Map<string, Run>();
  const listeners = new Set<() => void>();

  const apply = (action: TraceAction) => {
    const next = traceReducer(state, action);
    if (next === state) return;
    state = next;
    for (const listener of listeners) listener();
  };

  const observer: PluginRunObserver = {
    onRunStart: ({ runId, turnKey }) => {
      runs.set(runId, { turnKey, startedAt: now(), seq: 0 });
      apply({ type: 'attach', key: turnKey, turnId: runId });
      // The oldest runs go first (a Map keeps insertion order).
      for (const [old, run] of runs) {
        if (runs.size <= KEPT_RUNS) break;
        runs.delete(old);
        apply({ type: 'forget', key: run.turnKey });
      }
    },
    onEvent: (runId, event) => {
      const run = runs.get(runId);
      if (!run || run.ended) return;
      run.seq += 1;
      apply({
        type: 'appendFrame',
        key: run.turnKey,
        frame: frameOf(run.seq, now() - run.startedAt, event),
      });
    },
    onRunEnd: (runId, outcome) => {
      const run = runs.get(runId);
      if (run) run.ended = outcome;
    },
  };

  return {
    observer,
    subscribe: (listener: () => void) => {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    getState: () => state,
    /** The run that answers into a turn, while the monitor holds it. */
    runOf: (turnKey: string) => [...runs.entries()].find(([, run]) => run.turnKey === turnKey)?.[0],
    /** The trace events a read of the trace API brought, for a run the monitor holds. */
    appendTrace: (runId: string, events: readonly TraceEvent[]) => {
      const run = runs.get(runId);
      if (!run) return;
      for (const event of events) apply({ type: 'append', key: run.turnKey, event });
    },
    /** The last trace event the monitor has of a run: a read asks for what comes after it. */
    lastSeq: (runId: string) => traceFor(state, runId).at(-1)?.seq ?? 0,
    events: (key: string): TraceEvent[] => traceFor(state, key),
    frames: (key: string): AguiFrame[] => framesFor(state, key),
  };
}

export type MonitorStore = ReturnType<typeof createMonitorStore>;

/** One event of a run as the monitor's event log shows it: what crossed the wire, in order, with its size. */
function frameOf(seq: number, atMs: number, event: { type: string }): AguiFrame {
  const json = JSON.stringify(event);
  return {
    seq,
    atMs: Math.round(atMs),
    type: event.type,
    bytes: json.length,
    payload: JSON.parse(json) as unknown,
  };
}

/** The store the monitor plugin registers its run observer with. */
export const monitorStore = createMonitorStore();
