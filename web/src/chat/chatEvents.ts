import type { ChatStreamEvent, ConfirmationRequiredData } from '../api/types';
import type { SseFrame } from './sseParser';

/** The two names this system adds to the protocol, which carries them as custom events. */
const SOURCES = 'maf-lab/sources';
const TRACE = 'maf-lab/trace';

/**
 * Translates AG-UI frames into the events this app renders from.
 *
 * The protocol is the wire; the reducer's shape is ours. Anything the protocol carries that this screen has no
 * use for — a run starting, a text message opening, a custom event under a name we do not know — yields nothing,
 * which the protocol allows and which keeps an unknown event from breaking the rest of the run.
 */
export function toChatEvents(frame: SseFrame): ChatStreamEvent[] {
  let data: Record<string, unknown>;
  try {
    const parsed: unknown = JSON.parse(frame.data);
    if (typeof parsed !== 'object' || parsed === null) return [];
    data = parsed as Record<string, unknown>;
  } catch {
    return [];
  }

  switch (frame.event) {
    case 'TEXT_MESSAGE_CONTENT':
      return [{ type: 'text_delta', data: { text: String(data.delta ?? '') } }];

    case 'TOOL_CALL_START':
      return [
        {
          type: 'tool_call_started',
          data: {
            callId: String(data.toolCallId ?? ''),
            toolName: String(data.toolCallName ?? ''),
            argumentSummary: '',
          },
        },
      ];

    // The arguments arrive after the call opens; they are what the card shows, already reduced to identifiers.
    case 'TOOL_CALL_ARGS':
      return [
        {
          type: 'tool_call_started',
          data: {
            callId: String(data.toolCallId ?? ''),
            toolName: '',
            argumentSummary: String(data.delta ?? ''),
          },
        },
      ];

    case 'TOOL_CALL_RESULT': {
      // A tool result is structured content: which tool, how it went, how many sources — never the result itself.
      const result = parse(String(data.content ?? ''));
      return [
        {
          type: 'tool_call_finished',
          data: {
            callId: String(data.toolCallId ?? ''),
            toolName: String(result.tool ?? ''),
            resultSummary: String(result.summary ?? ''),
            sourceCount: Number(result.sourceCount ?? 0),
            isError: result.isError === true,
          },
        },
      ];
    }

    case 'CUSTOM':
      return custom(data);

    case 'RUN_FINISHED':
      return finished(data);

    case 'RUN_ERROR':
      return [
        {
          type: 'done',
          data: {
            conversationId: '',
            turnId: '',
            error: String(data.message ?? 'The run failed.'),
          },
        },
      ];

    default:
      return [];
  }
}

/** A tool result's content, when it is the structured kind this server sends. */
function parse(content: string): Record<string, unknown> {
  try {
    const value: unknown = JSON.parse(content);
    return typeof value === 'object' && value !== null
      ? (value as Record<string, unknown>)
      : { summary: content };
  } catch {
    return { summary: content };
  }
}

function custom(data: Record<string, unknown>): ChatStreamEvent[] {
  const value = data.value as Record<string, unknown> | undefined;
  if (!value) return [];
  switch (data.name) {
    case SOURCES:
      return [{ type: 'sources', data: { sources: (value.sources ?? []) as never } }];
    case TRACE:
      return [{ type: 'trace', data: value as never }];
    default:
      return [];
  }
}

function finished(data: Record<string, unknown>): ChatStreamEvent[] {
  const events: ChatStreamEvent[] = [];
  const outcome = data.outcome as Record<string, unknown> | undefined;
  const result = data.result as Record<string, unknown> | undefined;

  // A run that paused is waiting for a person. The card that renders this is the next change's work; the event
  // reaches the turn either way, so nothing about the pause is lost.
  if (outcome?.type === 'interrupt') {
    const interrupt = (outcome.interrupts as Record<string, unknown>[] | undefined)?.[0];
    const metadata = interrupt?.metadata as Record<string, unknown> | undefined;
    if (interrupt && metadata) {
      events.push({
        type: 'confirmation_required',
        data: {
          callId: String(interrupt.toolCallId ?? ''),
          toolName: String(metadata.tool ?? ''),
          adjustmentId: String(interrupt.id ?? ''),
          adjustment: metadata.adjustment as ConfirmationRequiredData['adjustment'],
          question: String(interrupt.message ?? ''),
          state: String(metadata.state ?? ''),
          expiresAt: interrupt.expiresAt ? String(interrupt.expiresAt) : null,
        },
      });
    }
  }

  events.push({
    type: 'done',
    data: {
      conversationId: String(data.threadId ?? ''),
      turnId: String(result?.turnId ?? ''),
    },
  });
  return events;
}
