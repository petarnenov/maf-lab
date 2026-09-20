import type {
  ChatStreamEvent,
  FeedbackKind,
  HistoryTurn,
  SourceRef,
  ConfirmationRequiredData,
} from '../api/types';
import { initialTraceState, traceReducer, type TraceState } from '../monitor/traceReducer';

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
  /** Which of the three faces this error wears. */
  errorKind?: FailureKind;
  /** Restored turns: false once the stored trace has passed its retention period. */
  traceAvailable?: boolean;
  /** Restored turns: feedback the user already sent for this turn. */
  feedbackKinds?: FeedbackKind[];
  /** True for turns loaded from conversation history rather than streamed in this session. */
  restored?: boolean;
  /** A write this turn put to the advisor, and what has become of it. */
  confirmation?: ConfirmationRequiredData;
  confirmationState?: ConfirmationState;
}

export type Turn = UserTurn | AssistantTurn;

export interface ChatState {
  conversationId?: string;
  turns: Turn[];
  streaming: boolean;
  /** Behind-the-scenes trace events, keyed by assistant turn (see traceReducer). */
  traces: TraceState;
}

/**
 * What has become of a write the turn is waiting on.
 * `waiting` — nobody has answered · `answering` — an answer is in flight · `applied` / `declined` — they did ·
 * `gone` — the server says it is no longer waiting · `expired` — too late to answer.
 */
/**
 * Which of three situations a failure is, because they call for three different reactions:
 * `unavailable` — something is down and trying again may work · `refused` — this is not yours to see, and why
 * is not explained · `unexpected` — anything else, said plainly.
 */
export type FailureKind = 'unavailable' | 'refused' | 'unexpected';

export type ConfirmationState =
  'waiting' | 'answering' | 'applied' | 'declined' | 'gone' | 'expired';

export type ChatAction =
  | { type: 'send'; userTurnId: string; assistantTurnId: string; text: string }
  | { type: 'event'; event: ChatStreamEvent }
  | { type: 'stream_error'; message: string; kind?: FailureKind }
  | { type: 'reset' }
  | { type: 'hydrate'; conversationId: string; turns: HistoryTurn[] }
  | { type: 'pending'; confirmation: ConfirmationRequiredData; expired: boolean }
  | { type: 'answering'; adjustmentId: string }
  | {
      type: 'answered';
      adjustmentId: string;
      outcome: Exclude<ConfirmationState, 'waiting' | 'answering'>;
      /** What the run said about it, which joins the conversation like any other answer. */
      said?: string;
    };

export const initialChatState: ChatState = {
  turns: [],
  streaming: false,
  traces: initialTraceState,
};

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
        errorKind: action.kind ?? 'unexpected',
        toolCalls: turn.toolCalls.map((c) =>
          c.status === 'running' ? { ...c, status: 'finished', isError: true } : c,
        ),
      }));

    case 'reset':
      return initialChatState;

    case 'hydrate':
      return {
        conversationId: action.conversationId,
        streaming: false,
        traces: initialTraceState,
        turns: action.turns.flatMap((t) => hydrateTurn(t)),
      };

    // A proposal found waiting after a reload belongs to the last assistant turn, which is where it was made.
    case 'pending':
      return updateLastAssistantTurn(state, (turn) => ({
        ...turn,
        confirmation: action.confirmation,
        confirmationState: action.expired ? 'expired' : 'waiting',
      }));

    case 'answering':
      return updateConfirmation(state, action.adjustmentId, 'answering');

    case 'answered': {
      const settled = updateConfirmation(state, action.adjustmentId, action.outcome);
      if (!action.said) return settled;
      return {
        ...settled,
        turns: [
          ...settled.turns,
          {
            id: `answer-${action.adjustmentId}`,
            role: 'assistant',
            text: action.said,
            toolCalls: [],
            sources: [],
            status: 'done',
          },
        ],
      };
    }
  }
}

/** True when a proposal can no longer be answered. The server decides again; this only stops asking. */
export function expired(
  confirmation: Pick<ConfirmationRequiredData, 'expiresAt'>,
  now = Date.now(),
): boolean {
  const at = confirmation.expiresAt;
  return typeof at === 'string' && Date.parse(at) <= now;
}

function updateConfirmation(
  state: ChatState,
  adjustmentId: string,
  next: ConfirmationState,
): ChatState {
  return {
    ...state,
    turns: state.turns.map((turn) =>
      turn.role === 'assistant' && turn.confirmation?.adjustmentId === adjustmentId
        ? { ...turn, confirmationState: next }
        : turn,
    ),
  };
}

function updateLastAssistantTurn(
  state: ChatState,
  update: (turn: AssistantTurn) => AssistantTurn,
): ChatState {
  const index = state.turns.map((t) => t.role).lastIndexOf('assistant');
  if (index < 0) return state;
  return {
    ...state,
    turns: state.turns.map((turn, i) => (i === index ? update(turn as AssistantTurn) : turn)),
  };
}

/** Converts a stored turn into the same user + assistant shape live turns use. */
export function hydrateTurn(turn: HistoryTurn): Turn[] {
  return [
    { id: `hu-${turn.turnId}`, role: 'user', text: turn.question },
    {
      id: `ha-${turn.turnId}`,
      role: 'assistant',
      text: turn.answer,
      turnId: turn.turnId,
      status: 'done',
      restored: true,
      traceAvailable: turn.traceAvailable,
      feedbackKinds: turn.feedbackKinds,
      sources: turn.sources.map((s) => ({
        docId: s.docId,
        sectionPath: s.sectionPath,
        sourcePath: s.sourcePath ?? '',
        snippet: s.snippet ?? '',
      })),
      toolCalls: turn.toolCalls.map((c, i) => ({
        callId: c.callId ?? `${turn.turnId}-${i}`,
        toolName: c.toolName,
        argumentSummary: c.argumentSummary,
        status: 'finished',
        // Turns stored before summaries were persisted fall back to the outcome.
        resultSummary: c.resultSummary ?? c.outcome,
        sourceCount: c.sourceCount,
        isError: c.outcome !== 'ok',
      })),
    },
  ];
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

    // The card that renders this belongs to the next change; the turn keeps it so nothing is lost meanwhile.
    case 'confirmation_required':
      return updateActiveTurn(state, (turn) => ({
        ...turn,
        confirmation: event.data,
        confirmationState: expired(event.data) ? 'expired' : 'waiting',
      }));

    case 'trace': {
      const active = activeTurn(state);
      if (!active) return state;
      return {
        ...state,
        traces: traceReducer(state.traces, { type: 'append', key: active.id, event: event.data }),
      };
    }

    case 'done': {
      const active = activeTurn(state);
      const traces = active
        ? traceReducer(state.traces, { type: 'attach', key: active.id, turnId: event.data.turnId })
        : state.traces;
      const next = updateActiveTurn({ ...state, traces }, (turn) => ({
        ...turn,
        turnId: event.data.turnId,
        status: event.data.error ? 'error' : 'done',
        error: event.data.error ?? undefined,
      }));
      return { ...next, streaming: false, conversationId: event.data.conversationId };
    }
  }
}

function activeTurn(state: ChatState): AssistantTurn | undefined {
  const last = state.turns[state.turns.length - 1];
  return last && last.role === 'assistant' && last.status === 'streaming' ? last : undefined;
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
