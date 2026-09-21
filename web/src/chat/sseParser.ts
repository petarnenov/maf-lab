/** @internal */
export interface SseFrame {
  event: string;
  data: string;
  id?: string;
}

/**
 * Incremental Server-Sent Events parser (WHATWG event-stream format).
 * Feed it arbitrary text chunks; it returns every frame completed so far and keeps partial input.
 */
export class SseParser {
  private buffer = '';
  private event = '';
  private data: string[] = [];
  private id: string | undefined;

  push(chunk: string): SseFrame[] {
    this.buffer += chunk;
    const frames: SseFrame[] = [];

    for (;;) {
      const match = /\r\n|\r|\n/.exec(this.buffer);
      if (!match) break;
      // A trailing '\r' may be the first half of a '\r\n' split across chunks: wait for more input.
      if (match[0] === '\r' && match.index === this.buffer.length - 1) break;

      const line = this.buffer.slice(0, match.index);
      this.buffer = this.buffer.slice(match.index + match[0].length);
      const frame = this.processLine(line);
      if (frame) frames.push(frame);
    }

    return frames;
  }

  /** Call at end of stream: dispatches a final frame that was not followed by a blank line. */
  flush(): SseFrame[] {
    const frames: SseFrame[] = [];
    if (this.buffer.length > 0) {
      const line = this.buffer.replace(/\r$/, '');
      this.buffer = '';
      const frame = this.processLine(line);
      if (frame) frames.push(frame);
    }
    const last = this.dispatch();
    if (last) frames.push(last);
    return frames;
  }

  private processLine(line: string): SseFrame | null {
    if (line === '') return this.dispatch();
    if (line.startsWith(':')) return null; // comment / keep-alive

    const colon = line.indexOf(':');
    const field = colon === -1 ? line : line.slice(0, colon);
    let value = colon === -1 ? '' : line.slice(colon + 1);
    if (value.startsWith(' ')) value = value.slice(1);

    switch (field) {
      case 'event':
        this.event = value;
        break;
      case 'data':
        this.data.push(value);
        break;
      case 'id':
        this.id = value;
        break;
      default:
        break; // 'retry' and unknown fields are ignored
    }
    return null;
  }

  private dispatch(): SseFrame | null {
    if (this.data.length === 0) {
      this.event = '';
      return null;
    }
    const frame: SseFrame = { event: this.event || 'message', data: this.data.join('\n') };
    if (this.id !== undefined) frame.id = this.id;
    this.event = '';
    this.data = [];
    return frame;
  }
}
