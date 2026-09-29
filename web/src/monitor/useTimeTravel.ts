import { useEffect, useReducer, useState, type Dispatch } from 'react';
import type { TraceEvent } from '../api/types';
import {
  effectiveCursor,
  initialTimeTravel,
  playbackDelay,
  timeTravelReducer,
  type TimeTravelAction,
  type TimeTravelState,
} from './timeTravel';

export interface TimeTravel {
  state: TimeTravelState;
  dispatch: Dispatch<TimeTravelAction>;
  /** Resolved step (0..events.length). */
  cursor: number;
}

/**
 * Time-travel state for one turn's events, including playback. `resetKey` identifies the turn: selecting another turn
 * starts over (following the newest step, which for a finished turn is its end).
 */
export function useTimeTravel(events: TraceEvent[], resetKey: string | undefined): TimeTravel {
  const [state, dispatch] = useReducer(timeTravelReducer, events.length, initialTimeTravel);
  const [turn, setTurn] = useState(resetKey);

  // Catch up during the render, not in an effect: an effect runs one render late, and for that render a following
  // cursor would sit one step behind the newest event — the chat would flash the turn as rewound
  // (fix-rewind-banner-flash). This render already resolves against the caught-up state.
  let current = state;
  if (turn !== resetKey) {
    current = initialTimeTravel(events.length);
    setTurn(resetKey);
    dispatch({ type: 'reset', count: events.length });
  } else if (state.count !== events.length) {
    current = timeTravelReducer(state, { type: 'eventsChanged', count: events.length });
    dispatch({ type: 'eventsChanged', count: events.length });
  }

  usePlayback(current, dispatch, events);

  return { state: current, dispatch, cursor: effectiveCursor(current) };
}

/** Advances the cursor at the recorded timing while playing; pausing or unmounting cancels the pending step. */
export function usePlayback(
  state: TimeTravelState,
  dispatch: Dispatch<TimeTravelAction>,
  events: TraceEvent[],
) {
  const cursor = effectiveCursor(state);
  const { playing, speed, compressWaits, count } = state;
  useEffect(() => {
    if (!playing) return;
    if (cursor >= count) {
      dispatch({ type: 'pause' });
      return;
    }
    const delay = playbackDelay(
      events.map((e) => e.atMs),
      cursor,
      speed,
      compressWaits,
    );
    const timer = setTimeout(() => dispatch({ type: 'tick' }), delay);
    return () => clearTimeout(timer);
  }, [playing, cursor, count, speed, compressWaits, events, dispatch]);
}
