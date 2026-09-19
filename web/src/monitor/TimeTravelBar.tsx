import type { Dispatch } from 'react';
import type { TraceEvent } from '../api/types';
import styles from './MonitorPanel.module.css';
import { SPEEDS, type Speed, type TimeTravelAction, type TimeTravelState } from './timeTravel';

export interface TimeTravelBarProps {
  events: TraceEvent[];
  state: TimeTravelState;
  dispatch: Dispatch<TimeTravelAction>;
  cursor: number;
  /** The turn is still streaming (enables "Back to live"). */
  live?: boolean;
}

/** Scrubber, step and playback controls for one turn's trace. */
export function TimeTravelBar({ events, state, dispatch, cursor, live }: TimeTravelBarProps) {
  const count = events.length;
  const current = cursor > 0 ? events[cursor - 1] : undefined;
  const valueText = current
    ? `step ${cursor} of ${count}: ${current.kind} — ${current.title}`
    : `step 0 of ${count}: before the turn started`;

  return (
    <div className={styles.timeTravel} role="group" aria-label="Time travel">
      <div className={styles.ttButtons}>
        <button
          type="button"
          aria-label="Jump to start"
          onClick={() => dispatch({ type: 'start' })}
          disabled={cursor === 0}
        >
          ⏮
        </button>
        <button
          type="button"
          aria-label="Step back"
          onClick={() => dispatch({ type: 'step', delta: -1 })}
          disabled={cursor === 0}
        >
          ◀
        </button>
        <button
          type="button"
          aria-label={state.playing ? 'Pause' : 'Play'}
          onClick={() => dispatch({ type: state.playing ? 'pause' : 'play' })}
          disabled={count === 0}
        >
          {state.playing ? '⏸' : '▶'}
        </button>
        <button
          type="button"
          aria-label="Step forward"
          onClick={() => dispatch({ type: 'step', delta: 1 })}
          disabled={cursor >= count}
        >
          ▶︎|
        </button>
        <button
          type="button"
          aria-label="Jump to end"
          onClick={() => dispatch({ type: 'end' })}
          disabled={cursor >= count}
        >
          ⏭
        </button>
      </div>
      <input
        className={styles.scrubber}
        type="range"
        min={0}
        max={count}
        step={1}
        value={cursor}
        aria-label="Trace step"
        aria-valuetext={valueText}
        onChange={(e) => dispatch({ type: 'seek', cursor: Number(e.target.value) })}
      />
      <span className={styles.ttStep} data-testid="tt-step">
        step {cursor} / {count}
      </span>
      <label className={styles.ttOption}>
        speed{' '}
        <select
          aria-label="Playback speed"
          value={state.speed}
          onChange={(e) => dispatch({ type: 'setSpeed', speed: Number(e.target.value) as Speed })}
        >
          {SPEEDS.map((s) => (
            <option key={s} value={s}>
              {s}×
            </option>
          ))}
        </select>
      </label>
      <label className={styles.ttOption}>
        <input
          type="checkbox"
          checked={state.compressWaits}
          onChange={() => dispatch({ type: 'toggleCompress' })}
        />{' '}
        compress waits
      </label>
      {live && state.cursor !== 'live' && (
        <button
          type="button"
          className={styles.ttLive}
          onClick={() => dispatch({ type: 'goLive' })}
        >
          Back to live
        </button>
      )}
      <span className={styles.visuallyHidden} aria-live="polite">
        {state.playing ? 'Playing' : 'Paused'}
      </span>
    </div>
  );
}
