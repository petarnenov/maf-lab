import { afterEach, describe, expect, it, vi } from 'vitest';
import type { ChatStreamEvent, HistoryTurn } from '../api/types';
import { chatReducer, initialChatState, type AssistantTurn, type ChatState } from './chatReducer';

const send = (state: ChatState = initialChatState) =>
  chatReducer(state, { type: 'send', userTurnId: 'u1', assistantTurnId: 'a1', text: 'How do I?' });

const apply = (state: ChatState, ...events: ChatStreamEvent[]) =>
  events.reduce((s, event) => chatReducer(s, { type: 'event', event }), state);

const assistant = (state: ChatState) => state.turns.at(-1) as AssistantTurn;

afterEach(() => vi.useRealTimers());

const started: ChatStreamEvent = {
  type: 'tool_call_started',
  data: {
    callId: 'c1',
    toolName: 'search_documents',
    argumentSummary: 'query="missing fee schedule"',
  },
};
const finished: ChatStreamEvent = {
  type: 'tool_call_finished',
  data: {
    callId: 'c1',
    toolName: 'search_documents',
    resultSummary: '3 snippets',
    sourceCount: 3,
    isError: false,
  },
};
const sources: ChatStreamEvent = {
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
const done: ChatStreamEvent = { type: 'done', data: { conversationId: 'conv-1', turnId: 't1' } };

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
    expect(turn.toolCalls).toHaveLength(0);
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
    traceAvailable: true,
  };

  it('restores finished turns with tools, sources, feedback and trace availability', () => {
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
      traceAvailable: true,
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
      traceAvailable: false,
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
    expect(turn.traceAvailable).toBe(false);
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
    state = chatReducer(state, {
      type: 'event',
      event: { type: 'confirmation_required', data: confirmation(expiresAt) },
    });
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
          traceAvailable: true,
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
});
