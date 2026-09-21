import { EventType } from '@ag-ui/core';
import { describe, expect, it } from 'vitest';
import type { ChatStreamEvent } from '../api/types';
import { readChatStream } from './readChatStream';
import { run, sse, streamResponse } from '../test/render';

describe('readChatStream', () => {
  it('emits events in order across split chunks and reports the run ending', async () => {
    const text =
      run.started() +
      run.toolCall('c1', 'search_documents', 'sourceTypes=docs').join('') +
      run.toolResult('c1', '0 snippets', 'search_documents') +
      run.sources([]) +
      run.done('c', 't');
    const chunks = [text.slice(0, 17), text.slice(17, 90), text.slice(90)];
    const events: ChatStreamEvent[] = [];

    const sawDone = await readChatStream(streamResponse(chunks).body!, (e) => events.push(e));

    expect(sawDone).toBe(true);
    expect(events.map((e) => e.type)).toEqual([
      'tool_call_started',
      'tool_call_started',
      'tool_call_finished',
      'sources',
      'done',
    ]);
  });

  it('skips events it has no use for and malformed JSON, and reports a run that never ended', async () => {
    const events: ChatStreamEvent[] = [];

    const sawDone = await readChatStream(
      streamResponse([
        run.started(),
        sse(EventType.STEP_STARTED, { stepName: 'x' }),
        'event: TEXT_MESSAGE_CONTENT\ndata: {oops\n\n',
        run.delta('a'),
      ]).body!,
      (e) => events.push(e),
    );

    expect(sawDone).toBe(false);
    expect(events).toEqual([{ type: 'text_delta', data: { text: 'a' } }]);
  });
});
