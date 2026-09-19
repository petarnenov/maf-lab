import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { TraceEvent } from '../api/types';
import { useTimeTravel } from './useTimeTravel';

const make = (atMs: number[]): TraceEvent[] =>
  atMs.map((at, i) => ({
    seq: i + 1,
    atMs: at,
    kind: 'k',
    title: `e${i + 1}`,
    durationMs: null,
    data: {},
    truncated: false,
  }));

describe('usePlayback', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('replays a 4 s turn at 10× in about 0.4 s and stops at the end', () => {
    const events = make([0, 1000, 2000, 3000, 4000]);
    const { result } = renderHook(() => useTimeTravel(events, 't1'));
    act(() => {
      result.current.dispatch({ type: 'toggleCompress' }); // real timing
      result.current.dispatch({ type: 'setSpeed', speed: 10 });
      result.current.dispatch({ type: 'play' });
    });
    expect(result.current.cursor).toBe(0);
    // Advance in 10 ms slices (each in its own act so the next step gets scheduled) and measure the replay.
    let elapsed = 0;
    while (result.current.state.playing && elapsed < 2000) {
      act(() => vi.advanceTimersByTime(10));
      elapsed += 10;
    }
    expect(result.current.cursor).toBe(5);
    expect(result.current.state.playing).toBe(false);
    expect(elapsed).toBeGreaterThanOrEqual(400);
    expect(elapsed).toBeLessThanOrEqual(450);
  });

  it('compresses long waits to one second before scaling', () => {
    const events = make([0, 30000]);
    const { result } = renderHook(() => useTimeTravel(events, 't1'));
    act(() => result.current.dispatch({ type: 'play' }));
    act(() => vi.advanceTimersByTime(0));
    expect(result.current.cursor).toBe(1);
    act(() => vi.advanceTimersByTime(999));
    expect(result.current.cursor).toBe(1);
    act(() => vi.advanceTimersByTime(1));
    expect(result.current.cursor).toBe(2);
  });

  it('pause cancels the pending step', () => {
    const events = make([0, 500, 1000]);
    const { result } = renderHook(() => useTimeTravel(events, 't1'));
    act(() => result.current.dispatch({ type: 'play' }));
    act(() => vi.advanceTimersByTime(0));
    act(() => result.current.dispatch({ type: 'pause' }));
    act(() => vi.advanceTimersByTime(5000));
    expect(result.current.cursor).toBe(1);
  });

  it('a different turn resets to following its newest step', () => {
    let events = make([0, 10, 20]);
    const { result, rerender } = renderHook(({ key }) => useTimeTravel(events, key), {
      initialProps: { key: 'a' },
    });
    act(() => result.current.dispatch({ type: 'start' }));
    expect(result.current.cursor).toBe(0);
    events = make([0, 10]);
    rerender({ key: 'b' });
    expect(result.current.state.cursor).toBe('live');
    expect(result.current.cursor).toBe(2);
  });
});
