import { EventType, type BaseEvent } from '@ag-ui/core';
import type { AguiFrame, ChatStreamEvent } from '../api/types';
import { toChatEvents } from './chatEvents';
import { SseParser } from './sseParser';

/** The name a trace event travels under, matching the server's. */
const TRACE = 'maf-lab/trace';

const encoder = new TextEncoder();

/**
 * Reads an SSE response body to the end, invoking `onEvent` for each chat event in order.
 * Returns true when a `done` event was received.
 *
 * `onFrame` sees every frame the run wrote, in arrival order and before that frame's chat events — including the
 * types this app maps to nothing and a frame whose JSON does not parse. It is the only place that can: by the time
 * `toChatEvents` is reached, the raw text and anything unparseable are already gone.
 */
export async function readChatStream(
  body: ReadableStream<Uint8Array>,
  onEvent: (event: ChatStreamEvent) => void,
  onFrame?: (frame: AguiFrame) => void,
): Promise<boolean> {
  const reader = body.getReader();
  const decoder = new TextDecoder();
  const parser = new SseParser();
  let sawDone = false;
  let seq = 0;
  let firstAt: number | undefined;

  const record = (name: string, data: string, parsed: unknown) => {
    if (!onFrame) return;
    const now = Date.now();
    firstAt ??= now;
    onFrame(frameOf(++seq, now - firstAt, name, data, parsed));
  };

  const emit = (frames: ReturnType<SseParser['push']>) => {
    for (const frame of frames) {
      let parsed: unknown;
      try {
        parsed = JSON.parse(frame.data);
      } catch {
        record(frame.event, frame.data, undefined);
        continue;
      }
      if (typeof parsed !== 'object' || parsed === null) {
        record(frame.event, frame.data, undefined);
        continue;
      }
      record(frame.event, frame.data, parsed);
      for (const event of toChatEvents(parsed as BaseEvent)) {
        if (event.type === 'done') sawDone = true;
        onEvent(event);
      }
    }
  };

  try {
    for (;;) {
      const { value, done } = await reader.read();
      if (done) break;
      emit(parser.push(decoder.decode(value, { stream: true })));
    }
    emit(parser.push(decoder.decode()));
    emit(parser.flush());
  } finally {
    reader.releaseLock();
  }

  return sawDone;
}

/**
 * One frame as the monitor shows it. A trace frame keeps the trace event's sequence number instead of its data:
 * the event itself is already in the panel's other tabs, and a second copy would double what a turn holds.
 */
function frameOf(
  seq: number,
  atMs: number,
  frameName: string,
  data: string,
  parsed: unknown,
): AguiFrame {
  const bytes = encoder.encode(data).length;
  if (parsed === undefined) {
    return { seq, atMs, type: frameName, bytes, unparsed: data };
  }
  const value = parsed as Record<string, unknown>;
  const type = typeof value.type === 'string' ? value.type : frameName;
  if (type !== EventType.CUSTOM) {
    return { seq, atMs, type, bytes, payload: parsed };
  }
  const name = String(value.name ?? '');
  if (name !== TRACE) {
    return { seq, atMs, type, name, bytes, payload: parsed };
  }
  const trace = value.value as { seq?: unknown } | undefined;
  const frame: AguiFrame = { seq, atMs, type, name, bytes };
  if (typeof trace?.seq === 'number') frame.traceSeq = trace.seq;
  return frame;
}
