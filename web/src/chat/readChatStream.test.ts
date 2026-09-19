import { describe, expect, it } from 'vitest';
import type { ChatStreamEvent } from '../api/types';
import { sse, streamResponse } from '../test/render';
import { readChatStream } from './readChatStream';

describe('readChatStream', () => {
  it('emits events in order across split chunks and reports done', async () => {
    const text =
      sse('tool_call_started', {
        callId: 'c1',
        toolName: 'search_documents',
        argumentSummary: 'q',
      }) +
      sse('tool_call_finished', {
        callId: 'c1',
        toolName: 'search_documents',
        resultSummary: 'ok',
        sourceCount: 1,
        isError: false,
      }) +
      sse('sources', { sources: [] }) +
      sse('done', { conversationId: 'c', turnId: 't' });
    const chunks = [text.slice(0, 17), text.slice(17, 90), text.slice(90)];
    const events: ChatStreamEvent[] = [];

    const sawDone = await readChatStream(streamResponse(chunks).body!, (e) => events.push(e));

    expect(sawDone).toBe(true);
    expect(events.map((e) => e.type)).toEqual([
      'tool_call_started',
      'tool_call_finished',
      'sources',
      'done',
    ]);
  });

  it('skips unknown events and malformed JSON, and reports a missing done', async () => {
    const events: ChatStreamEvent[] = [];
    const sawDone = await readChatStream(
      streamResponse([
        'event: ping\ndata: {}\n\n',
        'event: text_delta\ndata: {oops\n\n',
        sse('text_delta', { text: 'a' }),
      ]).body!,
      (e) => events.push(e),
    );
    expect(sawDone).toBe(false);
    expect(events).toEqual([{ type: 'text_delta', data: { text: 'a' } }]);
  });
});
