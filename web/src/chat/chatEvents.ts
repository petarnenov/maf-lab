import type { ChatStreamEvent } from '../api/types';
import type { SseFrame } from './sseParser';

const KNOWN = new Set<ChatStreamEvent['type']>([
  'text_delta',
  'tool_call_started',
  'tool_call_finished',
  'sources',
  'done',
]);

/** Maps an SSE frame to a typed chat event. Unknown event names and malformed JSON yield null. */
export function toChatEvent(frame: SseFrame): ChatStreamEvent | null {
  if (!KNOWN.has(frame.event as ChatStreamEvent['type'])) return null;
  try {
    const data: unknown = JSON.parse(frame.data);
    if (typeof data !== 'object' || data === null) return null;
    return { type: frame.event, data } as ChatStreamEvent;
  } catch {
    return null;
  }
}
