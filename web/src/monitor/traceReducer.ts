import type { AguiFrame, TraceEvent } from '../api/types';

/**
 * Live trace events keyed by the client-side assistant turn id (known when the message is sent). Trace events arrive
 * before `done`; when `done` reveals the server turn id, `attach` maps it to the same key so either id finds the trace.
 *
 * The run's AG-UI frames are kept the same way and under the same key: they are the same turn seen from the wire.
 */
export interface TraceState {
  byKey: Record<string, TraceEvent[]>;
  framesByKey: Record<string, AguiFrame[]>;
  keyByTurnId: Record<string, string>;
}

export type TraceAction =
  | { type: 'append'; key: string; event: TraceEvent }
  | { type: 'appendFrame'; key: string; frame: AguiFrame }
  | { type: 'attach'; key: string; turnId: string }
  | { type: 'reset' };

export const initialTraceState: TraceState = { byKey: {}, framesByKey: {}, keyByTurnId: {} };

export function traceReducer(state: TraceState, action: TraceAction): TraceState {
  switch (action.type) {
    case 'append': {
      const current = state.byKey[action.key] ?? [];
      if (current.some((e) => e.seq === action.event.seq)) return state;
      const next = insertBySeq(current, action.event);
      return { ...state, byKey: { ...state.byKey, [action.key]: next } };
    }
    // Frames arrive in the order the run wrote them, so they are appended as they come.
    case 'appendFrame': {
      const current = state.framesByKey[action.key] ?? [];
      if (current.some((f) => f.seq === action.frame.seq)) return state;
      return {
        ...state,
        framesByKey: { ...state.framesByKey, [action.key]: [...current, action.frame] },
      };
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

/** AG-UI frames for a client key or a server turn id, in the order the run wrote them. */
export function framesFor(state: TraceState, keyOrTurnId: string): AguiFrame[] {
  return (
    state.framesByKey[keyOrTurnId] ?? state.framesByKey[state.keyByTurnId[keyOrTurnId] ?? ''] ?? []
  );
}

function insertBySeq(events: TraceEvent[], event: TraceEvent): TraceEvent[] {
  const last = events[events.length - 1];
  if (!last || last.seq < event.seq) return [...events, event];
  const index = events.findIndex((e) => e.seq > event.seq);
  return [...events.slice(0, index), event, ...events.slice(index)];
}
