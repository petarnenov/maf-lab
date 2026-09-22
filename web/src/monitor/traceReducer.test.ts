import { describe, expect, it } from 'vitest';
import type { AguiFrame, TraceEvent } from '../api/types';
import { chatReducer, initialChatState } from '../chat/chatReducer';
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

  it('is fed by chat trace events and re-keyed when done arrives', () => {
    let state = chatReducer(initialChatState, {
      type: 'send',
      userTurnId: 'u-1',
      assistantTurnId: 'a-1',
      text: 'q',
    });
    state = chatReducer(state, { type: 'event', event: { type: 'trace', data: ev(2, 'intent') } });
    state = chatReducer(state, {
      type: 'event',
      event: { type: 'trace', data: ev(1, 'turn.start') },
    });
    state = chatReducer(state, {
      type: 'event',
      event: { type: 'done', data: { conversationId: 'c', turnId: 't_9' } },
    });
    // Events after done are not attributed to any turn.
    state = chatReducer(state, { type: 'event', event: { type: 'trace', data: ev(3) } });

    expect(traceFor(state.traces, 'a-1').map((e) => e.kind)).toEqual(['turn.start', 'intent']);
    expect(traceFor(state.traces, 't_9')).toHaveLength(2);
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

  it('collects the frames of the streaming turn and stops at done', () => {
    let state = chatReducer(initialChatState, {
      type: 'send',
      userTurnId: 'u-1',
      assistantTurnId: 'a-1',
      text: 'q',
    });
    for (const f of [frame(1, 'RUN_STARTED'), frame(2)]) {
      state = chatReducer(state, { type: 'event', event: { type: 'agui_frame', data: f } });
    }
    // The terminal frame is recorded while the turn still streams; `done` then closes it.
    state = chatReducer(state, {
      type: 'event',
      event: { type: 'agui_frame', data: frame(3, 'RUN_FINISHED') },
    });
    state = chatReducer(state, {
      type: 'event',
      event: { type: 'done', data: { conversationId: 'c', turnId: 't_9' } },
    });
    state = chatReducer(state, { type: 'event', event: { type: 'agui_frame', data: frame(4) } });

    expect(framesFor(state.traces, 'a-1').map((f) => f.type)).toEqual([
      'RUN_STARTED',
      'TEXT_MESSAGE_CONTENT',
      'RUN_FINISHED',
    ]);
    expect(framesFor(state.traces, 't_9')).toHaveLength(3);
  });
});
