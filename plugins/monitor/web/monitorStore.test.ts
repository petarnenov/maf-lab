import { describe, expect, it } from 'vitest';
import { createMonitorStore, KEPT_RUNS } from './monitorStore';
import type { TraceEvent } from './types';

const ev = (seq: number, kind = 'intent'): TraceEvent => ({
  seq,
  atMs: seq,
  kind,
  title: kind,
  durationMs: null,
  data: {},
  truncated: false,
});

function store() {
  let clock = 0;
  return createMonitorStore(() => (clock += 5));
}

describe('monitorStore', () => {
  it("records a run's official events as frames of its turn, found by the run id too", () => {
    const s = store();
    s.observer.onRunStart?.({ runId: 'r-1', turnKey: 'a-1' });
    s.observer.onEvent?.('r-1', { type: 'RUN_STARTED', runId: 'r-1' });
    s.observer.onEvent?.('r-1', { type: 'TEXT_MESSAGE_CONTENT', delta: 'hi' });
    s.observer.onRunEnd?.('r-1', 'finished');
    // A run that has ended writes no more frames.
    s.observer.onEvent?.('r-1', { type: 'TEXT_MESSAGE_CONTENT', delta: 'late' });

    const frames = s.frames('a-1');
    expect(frames.map((f) => [f.seq, f.type])).toEqual([
      [1, 'RUN_STARTED'],
      [2, 'TEXT_MESSAGE_CONTENT'],
    ]);
    expect(frames[1].payload).toEqual({ type: 'TEXT_MESSAGE_CONTENT', delta: 'hi' });
    expect(frames[1].bytes).toBe(JSON.stringify(frames[1].payload).length);
    expect(s.frames('r-1')).toBe(frames);
    expect(s.runOf('a-1')).toBe('r-1');
  });

  it('keeps the trace reads of a run in seq order and knows where the next read starts', () => {
    const s = store();
    s.observer.onRunStart?.({ runId: 'r-1', turnKey: 'a-1' });
    expect(s.lastSeq('r-1')).toBe(0);
    s.appendTrace('r-1', [ev(2), ev(1, 'turn.start')]);
    s.appendTrace('r-1', [ev(2), ev(3)]);
    // A run the monitor does not hold is not a turn on this screen.
    s.appendTrace('r-other', [ev(1)]);

    expect(s.events('a-1').map((e) => e.seq)).toEqual([1, 2, 3]);
    expect(s.lastSeq('r-1')).toBe(3);
    expect(s.events('r-other')).toEqual([]);
  });

  it('tells its subscribers of a change, and only of one', () => {
    const s = store();
    let calls = 0;
    const unsubscribe = s.subscribe(() => calls++);
    s.observer.onRunStart?.({ runId: 'r-1', turnKey: 'a-1' });
    s.appendTrace('r-1', [ev(1)]);
    s.appendTrace('r-1', [ev(1)]);
    unsubscribe();
    s.appendTrace('r-1', [ev(2)]);

    expect(calls).toBe(2);
  });

  it(`lets go of the oldest run past ${KEPT_RUNS}`, () => {
    const s = store();
    for (let i = 0; i <= KEPT_RUNS; i++) {
      s.observer.onRunStart?.({ runId: `r-${i}`, turnKey: `a-${i}` });
      s.appendTrace(`r-${i}`, [ev(1)]);
    }

    expect(s.runOf('a-0')).toBeUndefined();
    expect(s.events('a-0')).toEqual([]);
    expect(s.events(`a-${KEPT_RUNS}`)).toHaveLength(1);
  });
});
