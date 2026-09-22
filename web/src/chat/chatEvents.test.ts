import { EventType } from '@ag-ui/core';
import { describe, expect, it } from 'vitest';
import { toChatEvents } from './chatEvents';
import type { ConfirmationRequiredData } from '../api/types';

const event = (type: EventType, data: Record<string, unknown>) => ({ type, ...data });

describe('toChatEvents', () => {
  it('reads the answer from the protocol"s text message', () => {
    const events = toChatEvents(
      event(EventType.TEXT_MESSAGE_CONTENT, { messageId: 'm1', delta: 'Hello' }),
    );
    expect(events).toEqual([{ type: 'text_delta', data: { text: 'Hello' } }]);
  });

  it('opens a tool card on the call and closes it on the result', () => {
    const started = toChatEvents(
      event(EventType.TOOL_CALL_START, { toolCallId: 'c1', toolCallName: 'search_documents' }),
    );
    expect(started[0].type).toBe('tool_call_started');

    const finished = toChatEvents(
      event(EventType.TOOL_CALL_RESULT, { toolCallId: 'c1', content: '5 snippet(s)' }),
    );
    expect(finished[0]).toEqual({
      type: 'tool_call_finished',
      data: {
        callId: 'c1',
        toolName: '',
        resultSummary: '5 snippet(s)',
        sourceCount: 0,
        isError: false,
      },
    });
  });

  it('marks a failed call as an error', () => {
    const content = JSON.stringify({
      tool: 'send_email',
      summary: 'tool does not exist',
      sourceCount: 0,
      isError: true,
    });
    const [finished] = toChatEvents(
      event(EventType.TOOL_CALL_RESULT, { toolCallId: 'c1', content }),
    );
    expect(finished.type === 'tool_call_finished' && finished.data.isError).toBe(true);
  });

  it('falls back to plain text when a result is not structured', () => {
    const [finished] = toChatEvents(
      event(EventType.TOOL_CALL_RESULT, { toolCallId: 'c1', content: 'done' }),
    );
    expect(finished.type === 'tool_call_finished' && finished.data.resultSummary).toBe('done');
  });

  it('routes the sources and the trace by their custom names', () => {
    const sources = toChatEvents(
      event(EventType.CUSTOM, { name: 'maf-lab/sources', value: { sources: [{ docId: 'd1' }] } }),
    );
    expect(sources[0].type).toBe('sources');

    const trace = toChatEvents(
      event(EventType.CUSTOM, { name: 'maf-lab/trace', value: { seq: 1, kind: 'turn.start' } }),
    );
    expect(trace[0].type).toBe('trace');
  });

  it('ignores a custom event under a name it does not know', () => {
    expect(
      toChatEvents(event(EventType.CUSTOM, { name: 'someone-else/thing', value: {} })),
    ).toEqual([]);
  });

  it('ignores an event the protocol has and this screen does not render', () => {
    expect(toChatEvents(event(EventType.RUN_STARTED, { threadId: 't1', runId: 'r1' }))).toEqual([]);
    expect(toChatEvents(event(EventType.STEP_STARTED, { stepName: 'x' }))).toEqual([]);
  });

  it('ends the turn when the run finishes, carrying the thread and the turn', () => {
    const [done] = toChatEvents(
      event(EventType.RUN_FINISHED, {
        threadId: 'c1',
        runId: 'r1',
        outcome: { type: 'success' },
        result: { turnId: 't1' },
      }),
    );
    expect(done).toEqual({ type: 'done', data: { conversationId: 'c1', turnId: 't1' } });
  });

  it('reports a run that failed as an ended turn with its message', () => {
    const [done] = toChatEvents(
      event(EventType.RUN_ERROR, { message: 'The assistant could not finish.' }),
    );
    expect(done.type === 'done' && done.data.error).toBe('The assistant could not finish.');
  });

  it('keeps a paused run"s confirmation, which would otherwise vanish', () => {
    const adjustment = {
      adjustmentId: 'adj_1',
      accountId: 'A-1042',
      accountName: 'Ridgeline Family Trust',
      currentFee: 1200,
      amount: -200,
      resultingFee: 1000,
      currency: 'USD',
      periodStart: '2026-10-01',
      periodEnd: '2026-10-31',
    };
    const events = toChatEvents(
      event(EventType.RUN_FINISHED, {
        threadId: 'c1',
        runId: 'r1',
        result: { turnId: 't1' },
        outcome: {
          type: 'interrupt',
          interrupts: [
            {
              id: 'adj_1',
              message: 'Apply a fee adjustment of -200.00 USD to A-1042?',
              toolCallId: 'c1',
              metadata: { adjustment, state: 'opaque', tool: 'propose_fee_adjustment' },
            },
          ],
        },
      }),
    );

    expect(events.map((e) => e.type)).toEqual(['confirmation_required', 'done']);
    // The union now holds an event that carries no data, so this one is read through its own shape.
    const confirmation = (events[0] as { data: ConfirmationRequiredData }).data;
    expect(confirmation.adjustment.accountId).toBe('A-1042');
    expect(confirmation.state).toBe('opaque');
  });
  it("reads the model's reasoning, and ignores the reasoning events that carry nothing new", () => {
    expect(
      toChatEvents(
        event(EventType.REASONING_MESSAGE_CONTENT, { messageId: 'r1', delta: 'Let me' }),
      ),
    ).toEqual([{ type: 'reasoning_delta', data: { text: 'Let me' } }]);
    expect(toChatEvents(event(EventType.REASONING_END, { messageId: 'r1' }))).toEqual([
      { type: 'reasoning_end' },
    ]);

    // The block opens on the first delta and the run's end closes the turn, so these three say nothing to it.
    expect(toChatEvents(event(EventType.REASONING_START, { messageId: 'r1' }))).toEqual([]);
    expect(
      toChatEvents(
        event(EventType.REASONING_MESSAGE_START, { messageId: 'r1', role: 'reasoning' }),
      ),
    ).toEqual([]);
    expect(toChatEvents(event(EventType.REASONING_MESSAGE_END, { messageId: 'r1' }))).toEqual([]);
  });
});
