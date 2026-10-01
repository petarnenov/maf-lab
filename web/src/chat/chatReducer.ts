import {
  EventType,
  type ActivitySnapshotEvent,
  type BaseEvent,
  type ReasoningMessageContentEvent,
  type RunErrorEvent,
  type RunFinishedEvent,
  type RunStartedEvent,
  type StateSnapshotEvent,
  type StepStartedEvent,
  type TextMessageContentEvent,
  type ToolCallArgsEvent,
  type ToolCallResultEvent,
  type ToolCallStartEvent,
} from '@ag-ui/core';
import type {
  AguiFrame,
  DataCard,
  FocusAccount,
  FeedbackKind,
  HistoryTurn,
  SourceRef,
  ConfirmationRequiredData,
  TraceEvent,
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
  /** What the model thought on its way to the answer, as it streamed. Empty when it did not reason. */
  reasoning: string;
  /** How long the model spent reasoning, summed over every stretch of it. */
  reasoningMs?: number;
  /** Set only when a person opens or closes the block; their choice then wins for the rest of the turn. */
  reasoningOpen?: boolean;
  /** When the current stretch of reasoning started, while one is running. */
  reasoningSince?: number;
  toolCalls: ToolCallView[];
  sources: SourceRef[];
  /** Data cards the turn showed, in arrival order (add-activity-cards). */
  cards?: DataCard[];
  status: 'streaming' | 'done' | 'error';
  /** The run that is this turn, as the run's first event names it (agui-protocol-only). */
  runId?: string;
  /** What the run is doing right now, as its latest open step says; gone when the step finishes. */
  step?: string;
  /** A run answering a question an earlier turn asked: it records no turn of its own, so it has no turn id. */
  answer?: boolean;
  /** The turn this run recorded, known once it finished: its run id. Feedback needs it. */
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
  /**
   * The account in focus (add-focus-state): as the server last said, or as the user just chose. It is sent with the
   * next message, and the server's snapshot for that run replaces it.
   */
  focus: FocusAccount | null;
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
  /** One of the run's own events, exactly as the protocol defines it (agui-protocol-only). */
  | { type: 'event'; event: BaseEvent }
  /** One event of the run as it crossed the wire, for the monitor's event log. */
  | { type: 'frame'; frame: AguiFrame }
  /** The run's trace as written so far, read from the trace API while the run is live. */
  | { type: 'live_trace'; turnKey: string; events: TraceEvent[] }
  | { type: 'stream_error'; message: string; kind?: FailureKind }
  | { type: 'reset' }
  | { type: 'hydrate'; conversationId: string; turns: HistoryTurn[]; focus?: FocusAccount | null }
  /** The user chose an account to focus on, or cleared it; nothing runs until they send. */
  | { type: 'set_focus'; focus: FocusAccount | null }
  | { type: 'pending'; confirmation: ConfirmationRequiredData; expired: boolean }
  /** A person opened or closed a turn's reasoning; from then on the block is theirs, not the answer's. */
  | { type: 'toggle_reasoning'; turnId: string; open: boolean }
  | { type: 'answering'; adjustmentId: string }
  | {
      type: 'answered';
      adjustmentId: string;
      outcome: Exclude<ConfirmationState, 'waiting' | 'answering'>;
    }
  /**
   * Starts a resume run (Approve / Reject) as a new streaming assistant turn so the monitor panel
   * follows it, exactly as a normal send does. No user-visible text is added to the transcript.
   */
  | { type: 'start_answer'; assistantTurnId: string };

export const initialChatState: ChatState = {
  turns: [],
  streaming: false,
  traces: initialTraceState,
  focus: null,
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
            reasoning: '',
            toolCalls: [],
            sources: [],
            status: 'streaming',
          },
        ],
      };

    case 'event':
      return applyEvent(state, action.event);

    // A frame belongs to the turn whose run wrote it; the terminal frame is recorded before the turn closes.
    case 'frame': {
      const active = activeTurn(state);
      if (!active) return state;
      return {
        ...state,
        traces: traceReducer(state.traces, {
          type: 'appendFrame',
          key: active.id,
          frame: action.frame,
        }),
      };
    }

    case 'live_trace':
      return {
        ...state,
        traces: action.events.reduce(
          (traces, event) => traceReducer(traces, { type: 'append', key: action.turnKey, event }),
          state.traces,
        ),
      };

    case 'stream_error':
      return failed(state, action.message, action.kind ?? 'unexpected');

    case 'reset':
      return initialChatState;

    case 'hydrate':
      return {
        conversationId: action.conversationId,
        streaming: false,
        traces: initialTraceState,
        turns: action.turns.flatMap((t) => hydrateTurn(t)),
        focus: action.focus ?? null,
      };

    case 'set_focus':
      return { ...state, focus: action.focus };

    // A proposal found waiting after a reload belongs to the last assistant turn, which is where it was made.
    case 'pending':
      return updateLastAssistantTurn(state, (turn) => ({
        ...turn,
        confirmation: action.confirmation,
        confirmationState: action.expired ? 'expired' : 'waiting',
      }));

    case 'toggle_reasoning':
      return {
        ...state,
        turns: state.turns.map((turn) =>
          turn.role === 'assistant' && turn.id === action.turnId
            ? { ...turn, reasoningOpen: action.open }
            : turn,
        ),
      };

    case 'answering':
      return updateConfirmation(state, action.adjustmentId, 'answering');

    case 'start_answer':
      // Adds a streaming assistant turn for the resume run so the monitor panel follows it.
      // No user turn is added; the run is a resume, not a new message.
      return {
        ...state,
        streaming: true,
        turns: [
          ...state.turns,
          {
            id: action.assistantTurnId,
            role: 'assistant',
            text: '',
            reasoning: '',
            toolCalls: [],
            sources: [],
            status: 'streaming',
            answer: true,
          },
        ],
      };

    case 'answered': {
      return updateConfirmation(state, action.adjustmentId, action.outcome);
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
      // A restored turn's reasoning lives in its stored trace, not in the conversation history.
      reasoning: '',
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
        kind: s.kind,
        startLine: s.startLine,
        endLine: s.endLine,
        symbol: s.symbol,
        language: s.language,
      })),
      cards: turn.activities ?? [],
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

/**
 * One of the run's events, as the protocol defines it. An event this screen has no use for changes nothing, so any
 * agent that speaks AG-UI can be followed here (agui-protocol-only).
 */
function applyEvent(state: ChatState, event: BaseEvent): ChatState {
  switch (event.type) {
    // The run has begun, in this conversation: the run names both before anything can go wrong.
    case EventType.RUN_STARTED: {
      const started = event as RunStartedEvent;
      const next = updateActiveTurn(state, (turn) => ({ ...turn, runId: started.runId }));
      return { ...next, conversationId: started.threadId || state.conversationId };
    }

    // The server's word on the focus replaces whatever the client held, so a refused choice does not stay on screen.
    case EventType.STATE_SNAPSHOT: {
      const snapshot = (event as StateSnapshotEvent).snapshot as Record<string, unknown> | null;
      if (!snapshot || typeof snapshot !== 'object' || !('focus' in snapshot)) return state;
      return { ...state, focus: (snapshot.focus as FocusAccount | null) ?? null };
    }

    case EventType.STEP_STARTED:
      return updateActiveTurn(state, (turn) => ({
        ...turn,
        step: (event as StepStartedEvent).stepName,
      }));

    case EventType.STEP_FINISHED:
      return updateActiveTurn(state, (turn) => ({ ...turn, step: undefined }));

    // The answer's first text is what closes the reasoning block, and stops a clock still running.
    case EventType.TEXT_MESSAGE_CONTENT:
      return updateActiveTurn(state, (turn) => ({
        ...turn,
        ...stopThinking(turn),
        text: turn.text + (event as TextMessageContentEvent).delta,
      }));

    case EventType.REASONING_MESSAGE_CONTENT:
      return updateActiveTurn(state, (turn) => ({
        ...turn,
        reasoning: turn.reasoning + (event as ReasoningMessageContentEvent).delta,
        reasoningSince: turn.reasoningSince ?? Date.now(),
      }));

    case EventType.REASONING_END:
      return updateActiveTurn(state, (turn) => ({ ...turn, ...stopThinking(turn) }));

    case EventType.TOOL_CALL_START: {
      const start = event as ToolCallStartEvent;
      return updateActiveTurn(state, (turn) => {
        if (turn.toolCalls.some((c) => c.callId === start.toolCallId)) return turn;
        return {
          ...turn,
          toolCalls: [
            ...turn.toolCalls,
            {
              callId: start.toolCallId,
              toolName: start.toolCallName,
              argumentSummary: '',
              status: 'running',
            },
          ],
        };
      });
    }

    // The arguments as the server lets them travel: identifiers only, shown the way the audit writes them.
    case EventType.TOOL_CALL_ARGS: {
      const args = event as ToolCallArgsEvent;
      return updateActiveTurn(state, (turn) => ({
        ...turn,
        toolCalls: turn.toolCalls.map((c) =>
          c.callId === args.toolCallId
            ? { ...c, argumentSummary: summarizeArguments(c.argumentSummary, args.delta) }
            : c,
        ),
      }));
    }

    // A result says how the call went; one this system wrote also says what it found and where.
    case EventType.TOOL_CALL_RESULT: {
      const result = event as ToolCallResultEvent;
      const read = readResult(
        typeof result.content === 'string' ? result.content : JSON.stringify(result.content),
      );
      return updateActiveTurn(state, (turn) => {
        const known = turn.toolCalls.find((c) => c.callId === result.toolCallId);
        const finished: ToolCallView = {
          callId: result.toolCallId,
          toolName: known?.toolName ?? read.tool ?? '',
          argumentSummary: known?.argumentSummary ?? '',
          status: 'finished',
          resultSummary: read.summary,
          sourceCount: read.sourceCount,
          isError: read.isError,
        };
        return {
          ...turn,
          toolCalls: known
            ? turn.toolCalls.map((c) => (c.callId === result.toolCallId ? finished : c))
            : [...turn.toolCalls, finished],
          sources: mergeSources(turn.sources, read.sources),
        };
      });
    }

    // A snapshot for a card already shown replaces it, as the protocol says; a new one joins the end.
    case EventType.ACTIVITY_SNAPSHOT: {
      const activity = event as ActivitySnapshotEvent;
      const card: DataCard = {
        messageId: activity.messageId,
        activityType: activity.activityType,
        content: activity.content as Record<string, unknown>,
      };
      return updateActiveTurn(state, (turn) => {
        const cards = turn.cards ?? [];
        return {
          ...turn,
          cards: cards.some((c) => c.messageId === card.messageId)
            ? cards.map((c) => (c.messageId === card.messageId ? card : c))
            : [...cards, card],
        };
      });
    }

    case EventType.RUN_FINISHED:
      return finished(state, event as RunFinishedEvent);

    // A run refused or lost on its way to the agent says so with an HTTP status: it gets the face that status deserves,
    // never the status itself. Any other error is the agent's own short text.
    case EventType.RUN_ERROR: {
      const { message, code } = event as RunErrorEvent;
      const status = /\bHTTP (\d{3})\b/.exec(message);
      if (status) return failed(state, ...faceOf(Number(status[1])));
      // The stream ended before the run did: worth sending again.
      if (code === 'INCOMPLETE_STREAM')
        return failed(state, 'The answer stopped part-way. Send it again.', 'unavailable');
      return failed(state, message, 'unexpected');
    }

    default:
      return state;
  }
}

/** The run ended: paused on a question for a person, or done. A turn that recorded itself is known by its run. */
function finished(state: ChatState, event: RunFinishedEvent): ChatState {
  const interrupt = event.outcome?.type === 'interrupt' ? event.outcome.interrupts?.[0] : undefined;
  const confirmation = interrupt ? confirmationOf(interrupt) : undefined;
  const active = activeTurn(state);
  const turnId = active && !active.answer ? event.runId || active.runId : undefined;
  const traces =
    active && turnId
      ? traceReducer(state.traces, { type: 'attach', key: active.id, turnId })
      : state.traces;
  const next = updateActiveTurn({ ...state, traces }, (turn) => ({
    ...turn,
    ...stopThinking(turn),
    turnId,
    step: undefined,
    status: 'done',
    ...(confirmation
      ? {
          confirmation,
          confirmationState: expired(confirmation) ? 'expired' : ('waiting' as const),
        }
      : {}),
  }));
  return { ...next, streaming: false };
}

/**
 * What to say about a run that never reached its agent, and which of the three faces to say it with. A refusal says
 * only that it was refused: why is the server's business, and telling a caller would be telling them about someone
 * else's data.
 */
export function faceOf(status: number | undefined): [string, FailureKind] {
  if (status === 401) return ['Not signed in — pick a dev persona.', 'refused'];
  if (status === 403 || status === 404) return ['That conversation is not available.', 'refused'];
  if (status !== undefined)
    return ['The assistant is unavailable. Try again in a moment.', 'unavailable'];
  return ['The connection to the assistant was lost. Send it again.', 'unavailable'];
}

/** The run ended in error, or never got going: the turn keeps what it had and says so. */
export function failed(state: ChatState, message: string, kind: FailureKind): ChatState {
  return updateActiveTurn({ ...state, streaming: false }, (turn) => ({
    ...turn,
    ...stopThinking(turn),
    step: undefined,
    status: 'error',
    error: message,
    errorKind: kind,
    toolCalls: turn.toolCalls.map((c) =>
      c.status === 'running' ? { ...c, status: 'finished', isError: true } : c,
    ),
  }));
}

/**
 * The question a paused run put to a person, as the protocol's interrupt carries it. What it is about travels in the
 * interrupt's metadata; an interrupt without it is still a question this screen can show and answer.
 */
function confirmationOf(interrupt: {
  id: string;
  message?: string;
  toolCallId?: string;
  expiresAt?: string;
  metadata?: unknown;
}): ConfirmationRequiredData {
  const metadata = (interrupt.metadata ?? {}) as Record<string, unknown>;
  return {
    callId: interrupt.toolCallId ?? '',
    toolName: typeof metadata.tool === 'string' ? metadata.tool : '',
    adjustmentId: interrupt.id,
    adjustment: metadata.adjustment as ConfirmationRequiredData['adjustment'],
    question: interrupt.message ?? '',
    state: typeof metadata.state === 'string' ? metadata.state : '',
    expiresAt: interrupt.expiresAt ?? null,
  };
}

/** A call's arguments as they arrive (JSON, possibly in pieces), shown as "accountId=A-1043 runId=4417". */
function summarizeArguments(sofar: string, delta: string): string {
  const raw = (sofar.startsWith('{') ? sofar : '') + delta;
  try {
    const parsed: unknown = JSON.parse(raw);
    if (parsed && typeof parsed === 'object' && !Array.isArray(parsed)) {
      return Object.entries(parsed as Record<string, unknown>)
        .filter(([, v]) => v !== null && v !== undefined)
        .map(([k, v]) => `${k}=${typeof v === 'string' ? v : JSON.stringify(v)}`)
        .join(' ');
    }
  } catch {
    // Still arriving, or not JSON: kept as it came.
    return raw;
  }
  return raw;
}

interface ReadResult {
  tool?: string;
  summary: string;
  sourceCount?: number;
  isError: boolean;
  sources: SourceRef[];
}

/** A tool result's content: this system's `{ tool, summary, sourceCount, sources, isError }`, or anything else as text. */
function readResult(content: string): ReadResult {
  try {
    const value: unknown = JSON.parse(content);
    if (value && typeof value === 'object' && !Array.isArray(value)) {
      const r = value as Record<string, unknown>;
      return {
        tool: typeof r.tool === 'string' ? r.tool : undefined,
        summary: typeof r.summary === 'string' ? r.summary : 'done',
        sourceCount: typeof r.sourceCount === 'number' ? r.sourceCount : undefined,
        isError: r.isError === true,
        sources: Array.isArray(r.sources) ? (r.sources as SourceRef[]) : [],
      };
    }
  } catch {
    // Not structured: the text is the summary.
  }
  return {
    summary: content.length > 120 ? `${content.slice(0, 120)}…` : content || 'done',
    isError: false,
    sources: [],
  };
}

/** A turn's sources: each section once, in the order they were found. */
function mergeSources(sources: SourceRef[], found: SourceRef[]): SourceRef[] {
  if (found.length === 0) return sources;
  const key = (s: SourceRef) => `${s.docId}\u0000${s.sectionPath}`;
  const seen = new Set(sources.map(key));
  const added = found.filter((s) => !seen.has(key(s)) && !!seen.add(key(s)));
  return [...sources, ...added];
}

/** Closes the stretch of reasoning that is running, adding what it took to the turn's total. */
function stopThinking(turn: AssistantTurn): Partial<AssistantTurn> {
  if (turn.reasoningSince === undefined) return {};
  return {
    reasoningMs: (turn.reasoningMs ?? 0) + (Date.now() - turn.reasoningSince),
    reasoningSince: undefined,
  };
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
