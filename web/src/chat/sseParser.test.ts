import { describe, expect, it } from 'vitest';
import { SseParser } from './sseParser';

describe('SseParser', () => {
  it('parses multiple frames delivered in one chunk', () => {
    const parser = new SseParser();
    const frames = parser.push(
      'event: tool_call_started\ndata: {"callId":"1"}\n\nevent: text_delta\ndata: {"text":"hi"}\n\n',
    );
    expect(frames).toEqual([
      { event: 'tool_call_started', data: '{"callId":"1"}' },
      { event: 'text_delta', data: '{"text":"hi"}' },
    ]);
  });

  it('reassembles a frame split across arbitrary chunk boundaries', () => {
    const parser = new SseParser();
    const text = 'event: sources\ndata: {"sources":[]}\n\nevent: done\ndata: {"turnId":"t1"}\n\n';
    const frames = [];
    for (const ch of text) frames.push(...parser.push(ch));
    expect(frames).toEqual([
      { event: 'sources', data: '{"sources":[]}' },
      { event: 'done', data: '{"turnId":"t1"}' },
    ]);
  });

  it('does not emit a frame until the blank line arrives', () => {
    const parser = new SseParser();
    expect(parser.push('event: text_delta\ndata: {"text":"a"}\n')).toEqual([]);
    expect(parser.push('\n')).toEqual([{ event: 'text_delta', data: '{"text":"a"}' }]);
  });

  it('handles CRLF split between chunks, comments, and multi-line data', () => {
    const parser = new SseParser();
    const frames = [
      ...parser.push(': keep-alive\r'),
      ...parser.push('\nevent: x\r\ndata: line1\r\ndata: line2\r'),
      ...parser.push('\n\r\n'),
    ];
    expect(frames).toEqual([{ event: 'x', data: 'line1\nline2' }]);
  });

  it('defaults the event name to "message" and flushes a trailing frame', () => {
    const parser = new SseParser();
    expect(parser.push('data: tail')).toEqual([]);
    expect(parser.flush()).toEqual([{ event: 'message', data: 'tail' }]);
  });
});
