import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { framesFor, traceFor } from '../monitor/traceReducer';
import { readChatStream } from './readChatStream';
import { streamResponse } from '../test/render';
import { chatReducer, initialChatState, type AssistantTurn, type ChatState } from './chatReducer';

/** One run captured from the stack by `scripts/capture_ui_events.sh`. */
interface RecordedRun {
  id: string;
  what: string;
  /** When the run happened. Its frames carry real timestamps, so it is replayed at its own moment. */
  capturedAt: string;
  frames: { event: string; data: unknown }[];
  expect: {
    answer: string;
    /** What the model thought on its way to it; empty when it did not reason. */
    reasoning: string;
    toolCalls: string[];
    sources: number;
    traceSteps: number;
    /** Every frame the run wrote gets a row in the AG-UI view; none are merged away. */
    aguiFrames: number;
    pending: { id: string; reason: string } | null;
  };
}

const runs: RecordedRun[] = readFileSync(
  join(process.cwd(), '..', 'evals', 'ui-events.jsonl'),
  'utf8',
)
  .split('\n')
  .filter((line) => line.trim().length > 0)
  .map((line) => JSON.parse(line) as RecordedRun);

/** Replays a recorded run the way useChatStream does: wire → frames → events → reducer, at the time it happened. */
async function replay(run: RecordedRun): Promise<ChatState> {
  // A proposal's expiry is a real moment; a recording replayed on today's clock would always be past it.
  vi.setSystemTime(new Date(run.capturedAt));
  let state = chatReducer(initialChatState, {
    type: 'send',
    userTurnId: 'u1',
    assistantTurnId: 'a1',
    text: 'recorded',
  });
  const body = run.frames
    .map((f) => `event: ${f.event}\ndata: ${JSON.stringify(f.data)}\n\n`)
    .join('');
  await readChatStream(
    streamResponse([body]).body!,
    (event) => {
      state = chatReducer(state, { type: 'event', event });
    },
    (frame) => {
      state = chatReducer(state, { type: 'event', event: { type: 'agui_frame', data: frame } });
    },
  );
  return state;
}

function assistant(state: ChatState): AssistantTurn {
  const turn = state.turns.find((t) => t.role === 'assistant');
  if (!turn || turn.role !== 'assistant') throw new Error('no assistant turn');
  return turn;
}

describe('runs recorded from the running stack', () => {
  beforeEach(() => vi.useFakeTimers({ shouldAdvanceTime: true }));
  afterEach(() => vi.useRealTimers());

  // The reducer has always been tested against events a test author wrote. These are the frames the server
  // actually sent, so a change in what it emits fails here — which is when someone should look.
  it('has all three kinds of run recorded, each with when it happened', () => {
    expect(runs.every((r) => !Number.isNaN(Date.parse(r.capturedAt)))).toBe(true);
    expect(runs.map((r) => r.id)).toEqual([
      'plain-answer',
      'tool-call-with-sources',
      'pauses-for-confirmation',
    ]);
  });

  it.each(runs.map((run) => [run.id, run] as const))(
    '%s reaches the state it recorded',
    async (_id, run) => {
      const state = await replay(run);
      const turn = assistant(state);

      expect(turn.text).toBe(run.expect.answer);
      expect(turn.reasoning).toBe(run.expect.reasoning);
      expect(turn.toolCalls.map((c) => c.toolName)).toEqual(run.expect.toolCalls);
      expect(turn.sources).toHaveLength(run.expect.sources);
      expect(turn.status).toBe('done');

      // Every tool call the run started also finished; a card left spinning is a bug the browser would show.
      expect(turn.toolCalls.every((c) => c.status === 'finished')).toBe(true);

      if (run.expect.pending) {
        expect(turn.confirmation?.adjustmentId).toBe(run.expect.pending.id);
        expect(turn.confirmationState).toBe('waiting');
      } else {
        expect(turn.confirmation).toBeUndefined();
      }

      // The monitor saw the same run the chat did.
      expect(traceFor(state.traces, 'a1')).toHaveLength(run.expect.traceSteps);
      // And every frame that crossed the wire, including the ones the chat itself makes no use of.
      expect(framesFor(state.traces, 'a1')).toHaveLength(run.expect.aguiFrames);
      expect(framesFor(state.traces, 'a1').map((f) => f.seq)).toEqual(
        run.frames.map((_, i) => i + 1),
      );
    },
  );

  it('shows no message content in a tool card', async () => {
    const state = await replay(runs.find((r) => r.id === 'tool-call-with-sources')!);
    for (const call of assistant(state).toolCalls) {
      expect(call.resultSummary ?? '').not.toContain('{');
      expect(call.argumentSummary).not.toContain('fee schedule is missing');
    }
  });
});
