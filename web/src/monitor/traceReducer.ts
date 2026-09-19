import type { TraceEvent } from '../api/types';

/**
 * Live trace events keyed by the client-side assistant turn id (known when the message is sent). Trace events arrive
 * before `done`; when `done` reveals the server turn id, `attach` maps it to the same key so either id finds the trace.
 */
export interface TraceState {
  byKey: Record<string, TraceEvent[]>;
  keyByTurnId: Record<string, string>;
}

export type TraceAction =
  | { type: 'append'; key: string; event: TraceEvent }
  | { type: 'attach'; key: string; turnId: string }
  | { type: 'reset' };

export const initialTraceState: TraceState = { byKey: {}, keyByTurnId: {} };

export function traceReducer(state: TraceState, action: TraceAction): TraceState {
  switch (action.type) {
    case 'append': {
      const current = state.byKey[action.key] ?? [];
      if (current.some((e) => e.seq === action.event.seq)) return state;
      const next = insertBySeq(current, action.event);
      return { ...state, byKey: { ...state.byKey, [action.key]: next } };
    }
    case 'attach':
      if (state.keyByTurnId[action.turnId] === action.key) return state;
      return { ...state, keyByTurnId: { ...state.keyByTurnId, [action.turnId]: action.key } };
    case 'reset':
      return initialTraceState;
  }
}

/** Events for a client key or a server turn id, ordered by seq. */
export function traceFor(state: TraceState, keyOrTurnId: string): TraceEvent[] {
  return state.byKey[keyOrTurnId] ?? state.byKey[state.keyByTurnId[keyOrTurnId] ?? ''] ?? [];
}

function insertBySeq(events: TraceEvent[], event: TraceEvent): TraceEvent[] {
  const last = events[events.length - 1];
  if (!last || last.seq < event.seq) return [...events, event];
  const index = events.findIndex((e) => e.seq > event.seq);
  return [...events.slice(0, index), event, ...events.slice(index)];
}
