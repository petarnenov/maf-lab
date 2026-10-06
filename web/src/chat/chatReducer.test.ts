import { EventType, type BaseEvent } from '@ag-ui/core';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type {
  ConfirmationRequiredData,
  DataCard,
  FocusAccount,
  HistoryTurn,
  SourceRef,
} from '../api/types';
import {
  chatReducer,
  hydrateTurn,
  initialChatState,
  type AssistantTurn,
  type ChatState,
} from './chatReducer';

const send = (state: ChatState = initialChatState) =>
  chatReducer(state, { type: 'send', userTurnId: 'u1', assistantTurnId: 'a1', text: 'How do I?' });

/**
 * What happened in a run, as these tests say it; each is put to the reducer as the protocol's own events
 * (agui-protocol-only), which is all it reads.
 */
type Happened =
  | { type: 'text_delta'; data: { text: string } }
  | { type: 'reasoning_delta'; data: { text: string } }
  | { type: 'reasoning_end' }
  | {
      type: 'tool_call_started';
      data: { callId: string; toolName: string; argumentSummary: string };
    }
  | {
      type: 'tool_call_finished';
      data: {
        callId: string;
        toolName: string;
        resultSummary: string;
        sourceCount: number;
        isError: boolean;
      };
    }
  | { type: 'sources'; data: { sources: SourceRef[] } }
  | { type: 'state'; data: { focus: FocusAccount | null } }
  | { type: 'card'; data: DataCard }
  | { type: 'confirmation_required'; data: ConfirmationRequiredData }
  | { type: 'run_started'; data: { conversationId: string } }
  | { type: 'done'; data: { conversationId: string; turnId: string; error?: string | null } };

function official(happened: Happened): BaseEvent[] {
  switch (happened.type) {
    case 'text_delta':
      return [
        {
          type: EventType.TEXT_MESSAGE_CONTENT,
          messageId: 'm1',
          delta: happened.data.text,
        } as BaseEvent,
      ];
    case 'reasoning_delta':
      return [
        {
          type: EventType.REASONING_MESSAGE_CONTENT,
          messageId: 'r1',
          delta: happened.data.text,
        } as BaseEvent,
      ];
    case 'reasoning_end':
      return [{ type: EventType.REASONING_END, messageId: 'r1' } as BaseEvent];
    case 'tool_call_started':
      return [
        {
          type: EventType.TOOL_CALL_START,
          toolCallId: happened.data.callId,
          toolCallName: happened.data.toolName,
        } as BaseEvent,
        {
          type: EventType.TOOL_CALL_ARGS,
          toolCallId: happened.data.callId,
          delta: happened.data.argumentSummary,
        } as BaseEvent,
      ];
    case 'tool_call_finished':
      return [
        {
          type: EventType.TOOL_CALL_RESULT,
          toolCallId: happened.data.callId,
          messageId: happened.data.callId,
          content: JSON.stringify({
            tool: happened.data.toolName,
            summary: happened.data.resultSummary,
            sourceCount: happened.data.sourceCount,
            isError: happened.data.isError,
          }),
        } as BaseEvent,
      ];
    case 'sources':
      return [
        {
          type: EventType.TOOL_CALL_RESULT,
          toolCallId: 'search',
          messageId: 'search',
          content: JSON.stringify({
            tool: 'search_documents',
            summary: 'done',
            sources: happened.data.sources,
          }),
        } as BaseEvent,
      ];
    case 'state':
      return [
        { type: EventType.STATE_SNAPSHOT, snapshot: { focus: happened.data.focus } } as BaseEvent,
      ];
    case 'card':
      return [{ type: EventType.ACTIVITY_SNAPSHOT, ...happened.data } as BaseEvent];
    case 'confirmation_required':
      return [
        {
          type: EventType.RUN_FINISHED,
          threadId: 'conv-1',
          runId: 't1',
          outcome: {
            type: 'interrupt',
            interrupts: [
              {
                id: happened.data.adjustmentId,
                reason: 'approval_required',
                message: happened.data.question,
                toolCallId: happened.data.callId,
                expiresAt: happened.data.expiresAt ?? undefined,
                metadata: {
                  adjustment: happened.data.adjustment,
                  state: happened.data.state,
                  tool: happened.data.toolName,
                },
              },
            ],
          },
        } as BaseEvent,
      ];
    case 'run_started':
      return [
        {
          type: EventType.RUN_STARTED,
          threadId: happened.data.conversationId,
          runId: 'r1',
        } as BaseEvent,
      ];
    case 'done':
      return happened.data.error
        ? [{ type: EventType.RUN_ERROR, message: happened.data.error } as BaseEvent]
        : [
            {
              type: EventType.RUN_STARTED,
              threadId: happened.data.conversationId,
              runId: happened.data.turnId,
            } as BaseEvent,
            {
              type: EventType.RUN_FINISHED,
              threadId: happened.data.conversationId,
              runId: happened.data.turnId,
              outcome: { type: 'success' },
            } as BaseEvent,
          ];
  }
}

const apply = (state: ChatState, ...events: Happened[]) =>
  events.flatMap(official).reduce((s, event) => chatReducer(s, { type: 'event', event }), state);

const assistant = (state: ChatState) => state.turns.at(-1) as AssistantTurn;

afterEach(() => vi.useRealTimers());

const started: Happened = {
  type: 'tool_call_started',
  data: {
    callId: 'c1',
    toolName: 'search_documents',
    argumentSummary: 'query="missing fee schedule"',
  },
};
const finished: Happened = {
  type: 'tool_call_finished',
  data: {
    callId: 'c1',
    toolName: 'search_documents',
    resultSummary: '3 snippets',
    sourceCount: 3,
    isError: false,
  },
};
const sources: Happened = {
  type: 'sources',
  data: {
    sources: [
      {
        docId: 'd1',
        sectionPath: 'Billing > Fees',
        sourcePath: 'shared/docs/fees.md',
        snippet: 'x',
      },
    ],
  },
};
const done: Happened = { type: 'done', data: { conversationId: 'conv-1', turnId: 't1' } };

describe('chatReducer', () => {
  it('adds a user turn and a streaming assistant turn on send', () => {
    const state = send();
    expect(state.streaming).toBe(true);
    expect(state.turns.map((t) => t.role)).toEqual(['user', 'assistant']);
    expect(assistant(state).status).toBe('streaming');
  });

  it('tracks the tool card lifecycle: running then finished', () => {
    let state = apply(send(), started);
    expect(assistant(state).toolCalls).toEqual([
      expect.objectContaining({
        callId: 'c1',
        status: 'running',
        argumentSummary: 'query="missing fee schedule"',
      }),
    ]);
    state = apply(state, finished);
    expect(assistant(state).toolCalls).toEqual([
      expect.objectContaining({
        callId: 'c1',
        status: 'finished',
        resultSummary: '3 snippets',
        sourceCount: 3,
        argumentSummary: 'query="missing fee schedule"',
      }),
    ]);
  });

  it('applies a full ordered turn: tool events, sources, text, then done', () => {
    const state = apply(
      send(),
      started,
      finished,
      sources,
      { type: 'text_delta', data: { text: 'Step 1. ' } },
      { type: 'text_delta', data: { text: 'Step 2.' } },
      done,
    );
    const turn = assistant(state);
    expect(turn.text).toBe('Step 1. Step 2.');
    expect(turn.sources).toHaveLength(1);
    expect(turn.status).toBe('done');
    expect(turn.turnId).toBe('t1');
    expect(state.conversationId).toBe('conv-1');
    expect(state.streaming).toBe(false);
  });

  it('keeps sources that arrived before done and ignores events after done', () => {
    let state = apply(send(), sources, done);
    state = apply(state, { type: 'text_delta', data: { text: 'late' } }, started);
    const turn = assistant(state);
    expect(turn.sources).toHaveLength(1);
    expect(turn.text).toBe('');
    // Only the search that carried the sources; the call that started after the end is not there.
    expect(turn.toolCalls.map((c) => c.callId)).toEqual(['search']);
  });

  it('records a finished call even if its start event was missed', () => {
    const state = apply(send(), finished);
    expect(assistant(state).toolCalls[0]).toMatchObject({ callId: 'c1', status: 'finished' });
  });

  it('marks the turn as error when done carries an error', () => {
    const state = apply(send(), {
      type: 'done',
      data: {
        conversationId: 'c',
        turnId: 't',
        error: 'Document search is temporarily unavailable',
      },
    });
    expect(assistant(state)).toMatchObject({
      status: 'error',
      error: 'Document search is temporarily unavailable',
    });
  });

  it('keeps the conversation when a failed run names none', () => {
    let state = apply(send(), done);
    state = chatReducer(state, {
      type: 'send',
      userTurnId: 'u2',
      assistantTurnId: 'a2',
      text: 'next',
    });
    state = apply(state, {
      type: 'done',
      data: {
        conversationId: '',
        turnId: '',
        error: 'The assistant could not complete this answer.',
      },
    });
    expect(state.conversationId).toBe('conv-1');
    expect(state.streaming).toBe(false);
    expect(assistant(state)).toMatchObject({
      status: 'error',
      error: 'The assistant could not complete this answer.',
    });
  });

  it('takes the conversation from the start of the run, so a failed first turn keeps it', () => {
    let state = apply(send(), { type: 'run_started', data: { conversationId: 'conv-9' } });
    expect(state.conversationId).toBe('conv-9');
    state = apply(state, {
      type: 'done',
      data: {
        conversationId: '',
        turnId: '',
        error: 'The assistant could not complete this answer.',
      },
    });
    expect(state.conversationId).toBe('conv-9');
  });

  it('stream_error ends the turn and stops running tool cards', () => {
    let state = apply(send(), started);
    state = chatReducer(state, { type: 'stream_error', message: 'Connection lost.' });
    expect(state.streaming).toBe(false);
    expect(assistant(state)).toMatchObject({ status: 'error', error: 'Connection lost.' });
    expect(assistant(state).toolCalls[0]).toMatchObject({ status: 'finished', isError: true });
  });

  it('continues the same conversation on the next send', () => {
    let state = apply(send(), done);
    state = chatReducer(state, {
      type: 'send',
      userTurnId: 'u2',
      assistantTurnId: 'a2',
      text: 'next',
    });
    expect(state.conversationId).toBe('conv-1');
    expect(state.turns).toHaveLength(4);
  });
});

describe('hydrate', () => {
  const stored: HistoryTurn = {
    turnId: 't1',
    question: 'What if a fee schedule is missing?',
    answer: 'Assign it and re-run.',
    createdAt: '2026-09-19T10:00:00Z',
    toolCalls: [
      {
        callId: 'c1',
        toolName: 'search_documents',
        argumentSummary: 'query="fee"',
        outcome: 'ok',
        resultSummary: '2 snippets',
        sourceCount: 2,
      },
    ],
    sources: [{ docId: 'd1', sectionPath: 'Fees', sourcePath: 'shared/fees.md', snippet: 's' }],
    feedbackKinds: ['wrong_document'],
    reasoning: 'Fees first.',
    reasoningMs: 1200,
  };

  it('restores finished turns with tools, sources, feedback and reasoning', () => {
    const state = chatReducer(send(), {
      type: 'hydrate',
      conversationId: 'conv-9',
      turns: [stored],
    });
    expect(state.conversationId).toBe('conv-9');
    expect(state.streaming).toBe(false);
    expect(state.turns).toHaveLength(2);
    expect(state.turns[0]).toMatchObject({ role: 'user', text: stored.question });
    const turn = assistant(state);
    expect(turn).toMatchObject({
      turnId: 't1',
      text: stored.answer,
      status: 'done',
      restored: true,
      reasoning: 'Fees first.',
      reasoningMs: 1200,
      feedbackKinds: ['wrong_document'],
      sources: stored.sources,
    });
    expect(turn.toolCalls).toEqual([
      {
        callId: 'c1',
        toolName: 'search_documents',
        argumentSummary: 'query="fee"',
        status: 'finished',
        resultSummary: '2 snippets',
        sourceCount: 2,
        isError: false,
      },
    ]);
  });

  it('handles old rows without a result summary, call id or snippet', () => {
    const old: HistoryTurn = {
      ...stored,
      toolCalls: [
        {
          toolName: 'get_billing_run_status',
          argumentSummary: 'runId=4417',
          outcome: 'error',
          resultSummary: null,
          sourceCount: 0,
        },
      ],
      sources: [{ docId: 'd1', sectionPath: 'Fees', sourcePath: 'shared/fees.md', snippet: '' }],
      feedbackKinds: [],
      reasoning: null,
      reasoningMs: null,
    };
    const turn = assistant(
      chatReducer(initialChatState, { type: 'hydrate', conversationId: 'c', turns: [old] }),
    );
    expect(turn.toolCalls[0]).toMatchObject({
      callId: 't1-0',
      resultSummary: 'error',
      isError: true,
      status: 'finished',
    });
    expect(turn.sources[0].snippet).toBe('');
    expect(turn.reasoning).toBe('');
    expect(turn.reasoningMs).toBeUndefined();
  });
});

describe('a write waiting for a person', () => {
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
  const confirmation = (expiresAt?: string | null) => ({
    callId: 'c1',
    toolName: 'propose_fee_adjustment',
    adjustmentId: 'adj_1',
    adjustment,
    question: 'Apply a fee adjustment of -200.00 USD to A-1042?',
    state: 'opaque',
    expiresAt,
  });

  const proposing = (expiresAt?: string | null) => {
    let state = chatReducer(initialChatState, {
      type: 'send',
      userTurnId: 'u1',
      assistantTurnId: 'a1',
      text: 'adjust the fee',
    });
    state = apply(state, { type: 'confirmation_required', data: confirmation(expiresAt) });
    return state;
  };

  const card = (state: ChatState) =>
    state.turns.find((t) => t.role === 'assistant') as AssistantTurn;

  it('waits once a proposal arrives', () => {
    const state = proposing(new Date(Date.now() + 60_000).toISOString());
    expect(card(state).confirmationState).toBe('waiting');
    expect(card(state).confirmation?.adjustment.accountId).toBe('A-1042');
  });

  it('is already too late when the proposal arrives expired', () => {
    expect(card(proposing(new Date(Date.now() - 1).toISOString())).confirmationState).toBe(
      'expired',
    );
  });

  it('has no expiry to miss when the server sent none', () => {
    expect(card(proposing(null)).confirmationState).toBe('waiting');
  });

  it('moves through answering to what it became', () => {
    let state = proposing(new Date(Date.now() + 60_000).toISOString());
    state = chatReducer(state, { type: 'answering', adjustmentId: 'adj_1' });
    expect(card(state).confirmationState).toBe('answering');

    state = chatReducer(state, { type: 'answered', adjustmentId: 'adj_1', outcome: 'applied' });
    expect(card(state).confirmationState).toBe('applied');
  });

  it.each(['declined', 'gone', 'expired'] as const)('settles into %s', (outcome) => {
    let state = proposing(new Date(Date.now() + 60_000).toISOString());
    state = chatReducer(state, { type: 'answered', adjustmentId: 'adj_1', outcome });
    expect(card(state).confirmationState).toBe(outcome);
  });

  it('leaves another proposal alone', () => {
    let state = proposing(new Date(Date.now() + 60_000).toISOString());
    state = chatReducer(state, { type: 'answered', adjustmentId: 'adj_other', outcome: 'applied' });
    expect(card(state).confirmationState).toBe('waiting');
  });

  it('puts a proposal found after a reload on the last answer', () => {
    let state = chatReducer(initialChatState, {
      type: 'hydrate',
      conversationId: 'c1',
      turns: [
        {
          turnId: 't1',
          question: 'adjust the fee',
          answer: 'I have put it to you.',
          createdAt: '2026-09-20T12:00:00Z',
          toolCalls: [],
          sources: [],
          feedbackKinds: [],
        },
      ],
    });
    state = chatReducer(state, {
      type: 'pending',
      confirmation: confirmation(null),
      expired: false,
    });

    expect(card(state).confirmationState).toBe('waiting');
    expect(card(state).confirmation?.question).toContain('A-1042');
  });
  it('collects the reasoning of every stretch and times all of it', () => {
    vi.useFakeTimers();
    let state = send();
    state = apply(state, { type: 'reasoning_delta', data: { text: 'Let me look ' } });
    vi.advanceTimersByTime(1200);
    state = apply(state, { type: 'reasoning_delta', data: { text: 'that up.' } });
    state = apply(state, { type: 'reasoning_end' });

    expect(assistant(state).reasoning).toBe('Let me look that up.');
    expect(assistant(state).reasoningMs).toBe(1200);
    // Nobody has touched the block, so it is still the answer's to close.
    expect(assistant(state).reasoningOpen).toBeUndefined();

    // A second stretch after a tool call adds to the same reasoning and the same total.
    state = apply(state, started, finished);
    state = apply(state, { type: 'reasoning_delta', data: { text: ' Now I know.' } });
    vi.advanceTimersByTime(800);
    state = apply(state, { type: 'text_delta', data: { text: 'Assign it.' } });

    expect(assistant(state).reasoning).toBe('Let me look that up. Now I know.');
    expect(assistant(state).reasoningMs).toBe(2000);
    expect(assistant(state).text).toBe('Assign it.');
    expect(assistant(state).reasoningOpen).toBeUndefined();
  });

  it("keeps a person's choice about the block for the rest of the turn", () => {
    let state = send();
    state = apply(state, { type: 'reasoning_delta', data: { text: 'Thinking.' } });
    state = apply(state, { type: 'text_delta', data: { text: 'Answer.' } });

    state = chatReducer(state, { type: 'toggle_reasoning', turnId: 'a1', open: true });
    state = apply(state, { type: 'text_delta', data: { text: ' More.' } });

    expect(assistant(state).reasoningOpen).toBe(true);
    expect(assistant(state).text).toBe('Answer. More.');
  });

  it('leaves a turn that did not reason without any reasoning', () => {
    const state = apply(send(), { type: 'text_delta', data: { text: 'Straight to it.' } });
    expect(assistant(state).reasoning).toBe('');
    expect(assistant(state).reasoningMs).toBeUndefined();
  });

  it('keeps data cards in arrival order and replaces one sent again', () => {
    const card = (messageId: string, accountId: string): Happened => ({
      type: 'card',
      data: { messageId, activityType: 'maf-lab/holdings', content: { accountId } },
    });
    const state = apply(
      send(),
      card('card-1', 'A-1042'),
      card('card-2', 'A-1043'),
      card('card-1', 'A-1044'),
    );
    expect(assistant(state).cards?.map((c) => [c.messageId, c.content.accountId])).toEqual([
      ['card-1', 'A-1044'],
      ['card-2', 'A-1043'],
    ]);
  });

  it("restores a stored turn's cards, and none for a turn stored before cards", () => {
    const stored = (activities?: HistoryTurn['activities']): HistoryTurn => ({
      turnId: 't9',
      question: 'q',
      answer: 'a',
      createdAt: '2026-09-29T10:00:00Z',
      toolCalls: [],
      sources: [],
      feedbackKinds: [],
      activities,
    });
    const withCard = hydrateTurn(
      stored([{ messageId: 'card-1', activityType: 'maf-lab/accounts', content: { count: 1 } }]),
    )[1] as AssistantTurn;
    expect(withCard.cards).toHaveLength(1);
    expect((hydrateTurn(stored(undefined))[1] as AssistantTurn).cards).toEqual([]);
  });

  it('keeps the account in focus: chosen by the user, replaced by the server, restored with the conversation', () => {
    const chosen = chatReducer(initialChatState, {
      type: 'set_focus',
      focus: { accountId: 'A-1044' },
    });
    expect(chosen.focus).toEqual({ accountId: 'A-1044' });

    const refused = apply(send(chosen), {
      type: 'state',
      data: { focus: { accountId: 'A-1043' } },
    });
    expect(refused.focus).toEqual({ accountId: 'A-1043' });

    const restored = chatReducer(initialChatState, {
      type: 'hydrate',
      conversationId: 'c1',
      turns: [],
      focus: { accountId: 'A-1042' },
    });
    expect(restored.focus).toEqual({ accountId: 'A-1042' });
    expect(chatReducer(restored, { type: 'reset' }).focus).toBeNull();
  });
});

describe('a run the person stopped', () => {
  const aborted = {
    type: EventType.RUN_ERROR,
    message: 'Request aborted',
    code: 'abort',
  } as BaseEvent;
  const cancelled = {
    type: EventType.RUN_FINISHED,
    threadId: 'conv-1',
    runId: 'r1',
    outcome: { type: 'cancelled' },
  } as BaseEvent;
  const event = (state: ChatState, e: BaseEvent) => chatReducer(state, { type: 'event', event: e });

  it('asking to stop marks the turn and ends nothing', () => {
    const state = apply(send(), { type: 'text_delta', data: { text: 'The fee' } });
    const asked = chatReducer(state, { type: 'stop_requested' });
    expect(asked.streaming).toBe(true);
    expect(assistant(asked)).toMatchObject({
      status: 'streaming',
      stopping: true,
      text: 'The fee',
    });
    expect(assistant(asked).stopped).toBeUndefined();
    // The run's own events still reach its turn until it says it has stopped.
    const more = apply(asked, { type: 'text_delta', data: { text: ' schedule' } });
    expect(assistant(more).text).toBe('The fee schedule');
  });

  it('asking to stop with nothing running changes nothing', () => {
    const state = apply(send(), { type: 'text_delta', data: { text: 'all' } }, done);
    expect(chatReducer(state, { type: 'stop_requested' })).toBe(state);
  });

  it.each([
    ['an aborted run', aborted],
    ['a cancelled outcome', cancelled],
  ])('%s ends the turn stopped, keeping what it had, with no error', (_, terminal) => {
    vi.useFakeTimers();
    let state = apply(send(), { type: 'reasoning_delta', data: { text: 'Looking' } }, started);
    state = apply(state, { type: 'text_delta', data: { text: 'The fee schedule' } });
    state = chatReducer(state, { type: 'stop_requested' });
    vi.advanceTimersByTime(1_500);

    state = event(state, terminal);

    const turn = assistant(state);
    expect(state.streaming).toBe(false);
    expect(turn).toMatchObject({ status: 'done', stopped: true, text: 'The fee schedule' });
    expect(turn.stopping).toBeUndefined();
    expect(turn.reasoning).toBe('Looking');
    expect(turn.reasoningSince).toBeUndefined();
    expect(turn.step).toBeUndefined();
    expect(turn.error).toBeUndefined();
    expect(turn.errorKind).toBeUndefined();
    expect(turn.turnId).toBeUndefined();
    expect(turn.toolCalls).toEqual([
      expect.objectContaining({ callId: 'c1', status: 'finished', stopped: true }),
    ]);
    expect(turn.toolCalls[0].isError).toBeUndefined();
  });

  it('closes a tool card the runtime reports stopped as stopped, not failed', () => {
    const state = event(apply(send(), started), {
      type: EventType.TOOL_CALL_RESULT,
      toolCallId: 'c1',
      messageId: 'c1-result',
      role: 'tool',
      content: JSON.stringify({
        status: 'stopped',
        reason: 'stop_requested',
        message: 'Run stopped by user',
      }),
    } as BaseEvent);
    expect(assistant(state).toolCalls).toEqual([
      expect.objectContaining({
        callId: 'c1',
        status: 'finished',
        stopped: true,
        resultSummary: 'stopped',
      }),
    ]);
    expect(assistant(state).toolCalls[0].isError).toBe(false);
  });

  it('any other run error is still a failure with its face', () => {
    const state = event(apply(send(), started), {
      type: EventType.RUN_ERROR,
      message: 'Run ended without a terminal event',
      code: 'INCOMPLETE_STREAM',
    } as BaseEvent);
    expect(assistant(state)).toMatchObject({ status: 'error', errorKind: 'unavailable' });
    expect(assistant(state).stopped).toBeUndefined();
  });

  it('puts an answer that was on its way back to waiting', () => {
    let state = chatReducer(initialChatState, {
      type: 'send',
      userTurnId: 'u1',
      assistantTurnId: 'a1',
      text: 'adjust the fee',
    });
    state = apply(state, {
      type: 'confirmation_required',
      data: {
        callId: 'c1',
        toolName: 'propose_fee_adjustment',
        adjustmentId: 'adj_1',
        adjustment: {
          adjustmentId: 'adj_1',
          accountId: 'A-1042',
          accountName: 'Ridgeline Family Trust',
          currentFee: 1200,
          amount: -200,
          resultingFee: 1000,
          currency: 'USD',
          periodStart: '2026-10-01',
          periodEnd: '2026-10-31',
        },
        question: 'Apply a fee adjustment of -200.00 USD to A-1042?',
        state: 'opaque',
        expiresAt: null,
      },
    });
    state = chatReducer(state, { type: 'start_answer', assistantTurnId: 'a2' });
    state = chatReducer(state, { type: 'answering', adjustmentId: 'adj_1' });

    state = event(chatReducer(state, { type: 'stop_requested' }), aborted);

    const proposal = state.turns.find(
      (t) => t.role === 'assistant' && t.confirmation,
    ) as AssistantTurn;
    expect(proposal.confirmationState).toBe('waiting');
    expect(assistant(state)).toMatchObject({ id: 'a2', stopped: true, status: 'done' });
  });
});
