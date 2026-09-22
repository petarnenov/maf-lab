import { EventType } from '@ag-ui/core';
import { describe, expect, it } from 'vitest';
import type { AguiFrame, ChatStreamEvent } from '../api/types';
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

  it('hands every frame to onFrame, including the ones it maps to nothing', async () => {
    const text =
      run.started() +
      run.text('hi').join('') +
      run.trace({ seq: 4, atMs: 12, kind: 'intent', title: 'Intent', data: { intent: 'x' } }) +
      sse(EventType.CUSTOM, { name: 'maf-lab/unheard-of', value: { a: 1 } }) +
      run.done('c', 't');
    const frames: AguiFrame[] = [];

    await readChatStream(
      streamResponse([text]).body!,
      () => {},
      (f) => frames.push(f),
    );

    expect(frames.map((f) => f.type)).toEqual([
      EventType.RUN_STARTED,
      EventType.TEXT_MESSAGE_START,
      EventType.TEXT_MESSAGE_CONTENT,
      EventType.TEXT_MESSAGE_END,
      EventType.CUSTOM,
      EventType.CUSTOM,
      EventType.RUN_FINISHED,
    ]);
    expect(frames.map((f) => f.seq)).toEqual([1, 2, 3, 4, 5, 6, 7]);
    expect(frames.every((f) => f.bytes > 0 && f.atMs >= 0)).toBe(true);
    // A trace frame points at the trace event rather than carrying a second copy of it.
    expect(frames[4]).toMatchObject({ name: 'maf-lab/trace', traceSeq: 4 });
    expect(frames[4].payload).toBeUndefined();
    // A custom name this client knows nothing about keeps its payload.
    expect(frames[5]).toMatchObject({ name: 'maf-lab/unheard-of', payload: { value: { a: 1 } } });
  });

  it('records a frame whose JSON does not parse instead of dropping it', async () => {
    const frames: AguiFrame[] = [];

    await readChatStream(
      streamResponse(['event: TEXT_MESSAGE_CONTENT\ndata: {oops\n\n', run.delta('a')]).body!,
      () => {},
      (f) => frames.push(f),
    );

    expect(frames).toHaveLength(2);
    expect(frames[0]).toMatchObject({ type: 'TEXT_MESSAGE_CONTENT', unparsed: '{oops' });
    expect(frames[0].payload).toBeUndefined();
    expect(frames[1].type).toBe(EventType.TEXT_MESSAGE_CONTENT);
  });
});
