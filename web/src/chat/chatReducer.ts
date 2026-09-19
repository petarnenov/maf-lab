import type { ChatStreamEvent, SourceRef } from '../api/types';

export interface ToolCallView {
  callId: string;
  toolName: string;
  argumentSummary: string;
  status: 'running' | 'finished';
  resultSummary?: string;
  sourceCount?: number;
  isError?: boolean;
}

export interface UserTurn {
  id: string;
  role: 'user';
  text: string;
}

export interface AssistantTurn {
  id: string;
  role: 'assistant';
  text: string;
  toolCalls: ToolCallView[];
  sources: SourceRef[];
  status: 'streaming' | 'done' | 'error';
  /** Server-issued turn id, known once `done` arrives. Feedback needs it. */
  turnId?: string;
  error?: string;
}

export type Turn = UserTurn | AssistantTurn;

export interface ChatState {
  conversationId?: string;
  turns: Turn[];
  streaming: boolean;
}

export type ChatAction =
  | { type: 'send'; userTurnId: string; assistantTurnId: string; text: string }
  | { type: 'event'; event: ChatStreamEvent }
  | { type: 'stream_error'; message: string }
  | { type: 'reset' };

export const initialChatState: ChatState = { turns: [], streaming: false };

export function chatReducer(state: ChatState, action: ChatAction): ChatState {
  switch (action.type) {
    case 'send':
      return {
        ...state,
        streaming: true,
        turns: [
          ...state.turns,
          { id: action.userTurnId, role: 'user', text: action.text },
          {
            id: action.assistantTurnId,
            role: 'assistant',
            text: '',
            toolCalls: [],
            sources: [],
            status: 'streaming',
          },
        ],
      };

    case 'event':
      return applyEvent(state, action.event);

    case 'stream_error':
      return updateActiveTurn({ ...state, streaming: false }, (turn) => ({
        ...turn,
        status: 'error',
        error: action.message,
        toolCalls: turn.toolCalls.map((c) =>
          c.status === 'running' ? { ...c, status: 'finished', isError: true } : c,
        ),
      }));

    case 'reset':
      return initialChatState;
  }
}

function applyEvent(state: ChatState, event: ChatStreamEvent): ChatState {
  switch (event.type) {
    case 'text_delta':
      return updateActiveTurn(state, (turn) => ({ ...turn, text: turn.text + event.data.text }));

    case 'tool_call_started':
      return updateActiveTurn(state, (turn) => {
        if (turn.toolCalls.some((c) => c.callId === event.data.callId)) return turn;
        return {
          ...turn,
          toolCalls: [
            ...turn.toolCalls,
            {
              callId: event.data.callId,
              toolName: event.data.toolName,
              argumentSummary: event.data.argumentSummary,
              status: 'running',
            },
          ],
        };
      });

    case 'tool_call_finished':
      return updateActiveTurn(state, (turn) => {
        const finished: ToolCallView = {
          callId: event.data.callId,
          toolName: event.data.toolName,
          argumentSummary: '',
          status: 'finished',
          resultSummary: event.data.resultSummary,
          sourceCount: event.data.sourceCount,
          isError: event.data.isError,
        };
        const exists = turn.toolCalls.some((c) => c.callId === event.data.callId);
        return {
          ...turn,
          toolCalls: exists
            ? turn.toolCalls.map((c) =>
                c.callId === event.data.callId
                  ? { ...finished, argumentSummary: c.argumentSummary }
                  : c,
              )
            : [...turn.toolCalls, finished],
        };
      });

    case 'sources':
      return updateActiveTurn(state, (turn) => ({ ...turn, sources: event.data.sources }));

    case 'done': {
      const next = updateActiveTurn(state, (turn) => ({
        ...turn,
        turnId: event.data.turnId,
        status: event.data.error ? 'error' : 'done',
        error: event.data.error ?? undefined,
      }));
      return { ...next, streaming: false, conversationId: event.data.conversationId };
    }
  }
}

/** Applies `update` to the assistant turn currently streaming; events after `done` are ignored. */
function updateActiveTurn(
  state: ChatState,
  update: (turn: AssistantTurn) => AssistantTurn,
): ChatState {
  const index = state.turns.length - 1;
  const last = state.turns[index];
  if (!last || last.role !== 'assistant' || last.status !== 'streaming') return state;
  const turns = state.turns.slice();
  turns[index] = update(last);
  return { ...state, turns };
}
