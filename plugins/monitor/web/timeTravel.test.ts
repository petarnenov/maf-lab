import { describe, expect, it } from 'vitest';
import {
  effectiveCursor,
  initialTimeTravel,
  playbackDelay,
  timeTravelReducer,
  type TimeTravelAction,
  type TimeTravelState,
} from './timeTravel';

const run = (state: TimeTravelState, ...actions: TimeTravelAction[]) =>
  actions.reduce(timeTravelReducer, state);

describe('timeTravelReducer', () => {
  it('follows the newest step while live', () => {
    const s = run(
      initialTimeTravel(0),
      { type: 'eventsChanged', count: 3 },
      { type: 'eventsChanged', count: 5 },
    );
    expect(s.cursor).toBe('live');
    expect(effectiveCursor(s)).toBe(5);
  });

  it('leaving live pins the cursor while events keep arriving, goLive follows again', () => {
    let s = run(initialTimeTravel(4), { type: 'step', delta: -1 });
    expect(effectiveCursor(s)).toBe(3);
    s = run(s, { type: 'eventsChanged', count: 7 });
    expect(effectiveCursor(s)).toBe(3);
    s = run(s, { type: 'goLive' });
    expect(s.cursor).toBe('live');
    expect(effectiveCursor(s)).toBe(7);
  });

  it('seek, step, start and end clamp to the known steps', () => {
    const s = initialTimeTravel(5);
    expect(effectiveCursor(run(s, { type: 'seek', cursor: 99 }))).toBe(5);
    expect(effectiveCursor(run(s, { type: 'start' }, { type: 'step', delta: -1 }))).toBe(0);
    expect(effectiveCursor(run(s, { type: 'start' }, { type: 'step', delta: 3 }))).toBe(3);
    expect(effectiveCursor(run(s, { type: 'start' }, { type: 'end' }))).toBe(5);
  });

  it('play from the end restarts, ticks advance and stop at the last step', () => {
    let s = run(initialTimeTravel(2), { type: 'play' });
    expect(s).toMatchObject({ playing: true, cursor: 0 });
    s = run(s, { type: 'tick' });
    expect(s).toMatchObject({ playing: true, cursor: 1 });
    s = run(s, { type: 'tick' });
    expect(s).toMatchObject({ playing: false, cursor: 2 });
    expect(run(s, { type: 'tick' }).cursor).toBe(2);
  });

  it('manual stepping pauses playback; speed and compression toggle', () => {
    const s = run(initialTimeTravel(4), { type: 'play' }, { type: 'step', delta: 1 });
    expect(s.playing).toBe(false);
    expect(run(s, { type: 'setSpeed', speed: 5 }).speed).toBe(5);
    expect(run(s, { type: 'toggleCompress' }).compressWaits).toBe(false);
  });

  it('shrinking events clamps a pinned cursor', () => {
    const s = run(
      initialTimeTravel(6),
      { type: 'seek', cursor: 5 },
      { type: 'eventsChanged', count: 2 },
    );
    expect(effectiveCursor(s)).toBe(2);
  });
});

describe('playbackDelay', () => {
  const at = [0, 100, 4100];
  it('uses the recorded gap divided by speed', () => {
    expect(playbackDelay(at, 1, 1, false)).toBe(100);
    expect(playbackDelay(at, 2, 10, false)).toBe(400);
  });
  it('caps waits at one second before scaling when compressing', () => {
    expect(playbackDelay(at, 2, 1, true)).toBe(1000);
    expect(playbackDelay(at, 2, 10, true)).toBe(100);
  });
});
