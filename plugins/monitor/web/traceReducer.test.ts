import { describe, expect, it } from 'vitest';
import type { AguiFrame, TraceEvent } from './types';
import { framesFor, initialTraceState, traceFor, traceReducer } from './traceReducer';

const ev = (seq: number, kind = 'intent'): TraceEvent => ({
  seq,
  atMs: seq * 10,
  kind,
  title: kind,
  durationMs: null,
  data: {},
  truncated: false,
});

const frame = (seq: number, type = 'TEXT_MESSAGE_CONTENT'): AguiFrame => ({
  seq,
  atMs: seq,
  type,
  bytes: 10 * seq,
});

describe('traceReducer', () => {
  it('accumulates live events per turn in seq order and ignores duplicates', () => {
    let state = initialTraceState;
    for (const e of [ev(1), ev(3), ev(2), ev(3), ev(4)]) {
      state = traceReducer(state, { type: 'append', key: 'a-1', event: e });
    }
    state = traceReducer(state, { type: 'append', key: 'a-2', event: ev(1, 'turn.start') });
    expect(traceFor(state, 'a-1').map((e) => e.seq)).toEqual([1, 2, 3, 4]);
    expect(traceFor(state, 'a-2')).toHaveLength(1);
    expect(traceFor(state, 'missing')).toEqual([]);
  });

  it('attaches the server turn id so either id finds the trace', () => {
    let state = traceReducer(initialTraceState, { type: 'append', key: 'a-1', event: ev(1) });
    state = traceReducer(state, { type: 'attach', key: 'a-1', turnId: 't_server' });
    expect(traceFor(state, 't_server')).toBe(traceFor(state, 'a-1'));
    expect(traceReducer(state, { type: 'reset' })).toEqual(initialTraceState);
  });

  it("keeps a run's frames per turn, in arrival order, and finds them by either id", () => {
    let state = initialTraceState;
    for (const f of [frame(1, 'RUN_STARTED'), frame(2), frame(3), frame(2)]) {
      state = traceReducer(state, { type: 'appendFrame', key: 'a-1', frame: f });
    }
    state = traceReducer(state, { type: 'appendFrame', key: 'a-2', frame: frame(1) });
    state = traceReducer(state, { type: 'attach', key: 'a-1', turnId: 't_server' });

    expect(framesFor(state, 'a-1').map((f) => f.seq)).toEqual([1, 2, 3]);
    expect(framesFor(state, 't_server')).toBe(framesFor(state, 'a-1'));
    expect(framesFor(state, 'a-2')).toHaveLength(1);
    expect(framesFor(state, 'missing')).toEqual([]);
    expect(traceReducer(state, { type: 'reset' })).toEqual(initialTraceState);
  });

  it('forgets a turn with every id that found it', () => {
    let state = traceReducer(initialTraceState, { type: 'append', key: 'a-1', event: ev(1) });
    state = traceReducer(state, { type: 'appendFrame', key: 'a-1', frame: frame(1) });
    state = traceReducer(state, { type: 'append', key: 'a-2', event: ev(1) });
    state = traceReducer(state, { type: 'attach', key: 'a-1', turnId: 'r-1' });

    state = traceReducer(state, { type: 'forget', key: 'a-1' });

    expect(traceFor(state, 'r-1')).toEqual([]);
    expect(framesFor(state, 'a-1')).toEqual([]);
    expect(traceFor(state, 'a-2')).toHaveLength(1);
    expect(state.keyByTurnId).toEqual({});
  });
});
