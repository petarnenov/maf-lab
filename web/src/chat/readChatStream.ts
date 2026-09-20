import type { ChatStreamEvent } from '../api/types';
import { toChatEvents } from './chatEvents';
import { SseParser } from './sseParser';

/**
 * Reads an SSE response body to the end, invoking `onEvent` for each chat event in order.
 * Returns true when a `done` event was received.
 */
export async function readChatStream(
  body: ReadableStream<Uint8Array>,
  onEvent: (event: ChatStreamEvent) => void,
): Promise<boolean> {
  const reader = body.getReader();
  const decoder = new TextDecoder();
  const parser = new SseParser();
  let sawDone = false;

  const emit = (frames: ReturnType<SseParser['push']>) => {
    for (const frame of frames) {
      for (const event of toChatEvents(frame)) {
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
