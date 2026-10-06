/**
 * Time travel over one turn's trace: a cursor over its steps (0 = before the first event, N = after the last) plus
 * playback settings. `cursor: 'live'` follows the newest step; any manual move pins it to a number.
 */
export type Cursor = number | 'live';
export type Speed = 1 | 2 | 5 | 10;
export const SPEEDS: Speed[] = [1, 2, 5, 10];

export interface TimeTravelState {
  cursor: Cursor;
  playing: boolean;
  speed: Speed;
  /** Cap waits between steps at 1 s (before speed scaling) so long model calls do not stall a replay. */
  compressWaits: boolean;
  /** Number of events currently known for the turn. */
  count: number;
}

export type TimeTravelAction =
  | { type: 'seek'; cursor: number }
  | { type: 'step'; delta: number }
  | { type: 'start' }
  | { type: 'end' }
  | { type: 'play' }
  | { type: 'pause' }
  | { type: 'tick' }
  | { type: 'setSpeed'; speed: Speed }
  | { type: 'toggleCompress' }
  | { type: 'goLive' }
  | { type: 'eventsChanged'; count: number }
  | { type: 'reset'; count: number };

export const initialTimeTravel = (count = 0): TimeTravelState => ({
  cursor: 'live',
  playing: false,
  speed: 1,
  compressWaits: true,
  count,
});

/** The step the views should show: 'live' resolves to the newest step. */
export function effectiveCursor(state: TimeTravelState): number {
  return state.cursor === 'live' ? state.count : Math.min(Math.max(state.cursor, 0), state.count);
}

const clamp = (value: number, count: number) => Math.min(Math.max(value, 0), count);

export function timeTravelReducer(
  state: TimeTravelState,
  action: TimeTravelAction,
): TimeTravelState {
  switch (action.type) {
    case 'seek':
      return { ...state, cursor: clamp(action.cursor, state.count) };
    case 'step':
      return {
        ...state,
        playing: false,
        cursor: clamp(effectiveCursor(state) + action.delta, state.count),
      };
    case 'start':
      return { ...state, playing: false, cursor: 0 };
    case 'end':
      return { ...state, playing: false, cursor: state.count };
    case 'play': {
      if (state.count === 0) return state;
      const at = effectiveCursor(state);
      // Playing from the end replays the turn from the beginning.
      return { ...state, playing: true, cursor: at >= state.count ? 0 : at };
    }
    case 'pause':
      return { ...state, playing: false };
    case 'tick': {
      if (!state.playing) return state;
      const next = clamp(effectiveCursor(state) + 1, state.count);
      return { ...state, cursor: next, playing: next < state.count };
    }
    case 'setSpeed':
      return { ...state, speed: action.speed };
    case 'toggleCompress':
      return { ...state, compressWaits: !state.compressWaits };
    case 'goLive':
      return { ...state, playing: false, cursor: 'live' };
    case 'eventsChanged':
      if (action.count === state.count) return state;
      return {
        ...state,
        count: action.count,
        cursor: state.cursor === 'live' ? 'live' : clamp(state.cursor, action.count),
      };
    case 'reset':
      return initialTimeTravel(action.count);
  }
}

/** Delay before the next step during playback: the recorded gap to it, optionally capped, divided by the speed. */
export function playbackDelay(
  atMs: readonly number[],
  cursor: number,
  speed: Speed,
  compressWaits: boolean,
): number {
  const next = atMs[cursor] ?? 0;
  const previous = cursor > 0 ? (atMs[cursor - 1] ?? 0) : 0;
  let gap = Math.max(next - previous, 0);
  if (compressWaits) gap = Math.min(gap, 1000);
  return gap / speed;
}
