import { useEffect, useReducer, type Dispatch } from 'react';
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

  useEffect(() => {
    dispatch({ type: 'reset', count: events.length });
    // Only when the turn changes; event growth is handled below.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [resetKey]);

  useEffect(() => {
    dispatch({ type: 'eventsChanged', count: events.length });
  }, [events.length]);

  usePlayback(state, dispatch, events);

  return { state, dispatch, cursor: effectiveCursor(state) };
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
