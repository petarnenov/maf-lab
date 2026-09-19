import { describe, expect, it } from 'vitest';
import type { ChatStreamEvent } from '../api/types';
import { chatReducer, initialChatState, type AssistantTurn, type ChatState } from './chatReducer';

const send = (state: ChatState = initialChatState) =>
  chatReducer(state, { type: 'send', userTurnId: 'u1', assistantTurnId: 'a1', text: 'How do I?' });

const apply = (state: ChatState, ...events: ChatStreamEvent[]) =>
  events.reduce((s, event) => chatReducer(s, { type: 'event', event }), state);

const assistant = (state: ChatState) => state.turns.at(-1) as AssistantTurn;

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
