import type { AbstractAgent } from '@ag-ui/client';
import type { BaseEvent } from '@ag-ui/core';
import { useCopilotKit } from '@copilotkit/react-core/v2/context';
import { useQueryClient } from '@tanstack/react-query';
import { useCallback, useEffect, useReducer, useRef } from 'react';
import { authHeaders } from '../api/client';
import type { ConversationDetail, FocusAccount, PendingProposal } from '../api/types';
import { agentNamed } from '../agents/agents';
import { useAuth } from '../auth/useAuth';
import type { PluginRunObserver } from '../plugins/api';
import { usePlugins } from '../plugins/context';
import { contributions } from '../plugins/registry';
import { chatReducer, initialChatState } from './chatReducer';
import { expired, faceOf, type FailureKind } from './chatReducer';

let counter = 0;
const nextId = (prefix: string) =>
  `${prefix}-${Date.now().toString(36)}-${(counter++).toString(36)}`;

/** The chat agent, as CopilotKit's runtime names it (agui-protocol-only). */
export const CHAT_AGENT = 'chat';

/** A new conversation's id: AG-UI clients name their own threads, and the server claims it for this user. */
const newThreadId = () => `c_${crypto.randomUUID().replaceAll('-', '')}`;

/**
 * The chat, as the user sees it, fed by the chat agent through CopilotKit (agui-protocol-only): every event the run
 * carries goes to the screen exactly as the protocol defines it, and to the run observers of the plugins in use.
 */
export function useChatStream() {
  const { session } = useAuth();
  const token = session?.token ?? null;
  const { copilotkit } = useCopilotKit();
  const [state, dispatch] = useReducer(chatReducer, initialChatState);
  const queryClient = useQueryClient();
  const plugins = usePlugins();
  // Read when a run starts, so a run is observed by the plugins in use as it began.
  const observersRef = useRef<PluginRunObserver[]>([]);
  useEffect(() => {
    observersRef.current = contributions(plugins, 'runObservers').map(({ item }) => item);
  }, [plugins]);
  const agentRef = useRef<AbstractAgent | null>(null);
  const conversationRef = useRef<string | undefined>(undefined);
  const focusRef = useRef<FocusAccount | null>(null);

  useEffect(() => {
    conversationRef.current = state.conversationId;
  }, [state.conversationId]);

  useEffect(() => {
    focusRef.current = state.focus;
  }, [state.focus]);

  /**
   * Stops the run in progress, through the protocol's client: CopilotKit asks its runtime to stop the thread, which
   * ends the run's request to the agent, which cancels the run there.
   */
  const stop = useCallback(() => {
    const agent = agentRef.current;
    agentRef.current = null;
    if (agent?.isRunning) copilotkit.stopAgent({ agent });
  }, [copilotkit]);

  useEffect(() => stop, [stop]);

  /** The agent whose stop was asked for: one stop per run. */
  const stopAskedRef = useRef<AbstractAgent | null>(null);

  /**
   * The person asks for the run in progress to stop (Esc): CopilotKit's own stop for the run, and nothing else. The
   * run goes on reaching its turn until its own terminal event says it has stopped (agui-protocol-only).
   */
  const cancel = useCallback(() => {
    const agent = agentRef.current;
    if (!agent?.isRunning || stopAskedRef.current === agent) return;
    stopAskedRef.current = agent;
    dispatch({ type: 'stop_requested' });
    copilotkit.stopAgent({ agent });
  }, [copilotkit]);

  /**
   * One run of the chat agent. Resolves with whether the run ended the way the protocol says a run ends, or with
   * 'stopped' when its terminal event said it was stopped.
   */
  const run = useCallback(
    async (
      assistantTurnId: string,
      prepare: (agent: AbstractAgent) => void,
      resume?: { interruptId: string; status: 'resolved'; payload: unknown }[],
      observe?: (event: BaseEvent) => void,
    ): Promise<boolean | 'stopped'> => {
      stop();
      // A run of its own: the run it replaces may still be winding down on the agent it used.
      const agent: AbstractAgent | undefined = (await agentNamed(copilotkit, CHAT_AGENT))?.clone();
      if (!agent) {
        dispatch({
          type: 'stream_error',
          message: 'The assistant is unavailable. Try again in a moment.',
          kind: 'unavailable',
        });
        return false;
      }
      agentRef.current = agent;
      const runId = nextId('r');
      prepare(agent);
      const observers = observersRef.current;
      notify(observers, (o) =>
        o.onRunStart?.({ runId, turnKey: assistantTurnId, conversationId: agent.threadId }),
      );
      let ended = false;
      let stoppedRun = false;
      let failure: [string, FailureKind] | null = null;
      const subscription = agent.subscribe({
        onEvent: ({ event }: { event: BaseEvent }) => {
          // A run another one replaced may still be winding down; what it says now belongs to no turn on screen.
          if (agentRef.current !== agent) return;
          dispatch({ type: 'event', event });
          notify(observers, (o) =>
            o.onEvent?.(runId, event as BaseEvent & Record<string, unknown>),
          );
          observe?.(event);
          if (event.type === 'RUN_FINISHED' || event.type === 'RUN_ERROR') {
            ended = true;
            stoppedRun = isStop(event);
          }
        },
        onRunFailed: ({ error }: { error: Error }) => {
          failure = failureOf(error);
        },
      });
      try {
        await copilotkit.runAgent({ agent, runId, ...(resume ? { resume } : {}) });
      } catch (error) {
        failure ??= failureOf(error);
      } finally {
        subscription.unsubscribe();
      }
      const replaced = agentRef.current !== agent;
      const outcome = replaced || stoppedRun ? 'stopped' : ended ? 'finished' : 'failed';
      notify(observers, (o) => o.onRunEnd?.(runId, outcome));
      // A finished run changes the history list (new conversation, last activity, turn count).
      if (outcome === 'finished')
        void queryClient.invalidateQueries({ queryKey: ['conversations'] });
      if (replaced) return 'stopped';
      agentRef.current = null;
      if (stoppedRun) return 'stopped';
      if (!ended) {
        const [message, kind] = failure ?? [
          'The answer stopped part-way. Send it again.',
          'unavailable',
        ];
        dispatch({ type: 'stream_error', message, kind });
      }
      return ended;
    },
    [copilotkit, queryClient, stop],
  );

  const send = useCallback(
    async (message: string) => {
      const text = message.trim();
      if (!text) return;
      const assistantTurnId = nextId('a');
      dispatch({ type: 'send', userTurnId: nextId('u'), assistantTurnId, text });
      const threadId = conversationRef.current ?? newThreadId();
      conversationRef.current = threadId;
      await run(assistantTurnId, (agent) => {
        agent.threadId = threadId;
        // The run's AG-UI state: the account in focus as this screen holds it (add-focus-state).
        agent.setState({ focus: focusRef.current });
        agent.addMessage({ id: nextId('u'), role: 'user', content: text });
      });
    },
    [run],
  );

  const reset = useCallback(() => {
    stop();
    conversationRef.current = undefined;
    focusRef.current = null;
    dispatch({ type: 'reset' });
  }, [stop]);

  /**
   * A person's answer to a waiting write: a run that resumes the interrupt. The answer arrives in the
   * conversation like any other, and what became of the proposal is read from what the run said.
   */
  const answer = useCallback(
    async (adjustmentId: string, approve: boolean) => {
      const conversationId = conversationRef.current;
      if (!conversationId) return;

      // The same answer to the same proposal is the same attempt, however many times the stream drops on the
      // way. The key says so; the server then replays its first answer rather than applying anything twice.
      const idempotencyKey = keyFor(adjustmentId, approve);
      const assistantTurnId = nextId('a');
      // Opens a streaming assistant turn so the monitor panel follows the answer run.
      dispatch({ type: 'start_answer', assistantTurnId });
      dispatch({ type: 'answering', adjustmentId });

      let said = '';
      const ended = await run(
        assistantTurnId,
        (agent) => {
          agent.threadId = conversationId;
          agent.setMessages([]);
        },
        [{ interruptId: adjustmentId, status: 'resolved', payload: { approve, idempotencyKey } }],
        (event) => {
          if (event.type === 'TEXT_MESSAGE_CONTENT')
            said += (event as BaseEvent & { delta: string }).delta;
        },
      );
      // A stopped answer is waiting again; the reducer already put it back.
      if (ended === 'stopped') return;
      dispatch({
        type: 'answered',
        adjustmentId,
        outcome: ended ? outcomeOf(said, approve) : 'gone',
      });
    },
    [run],
  );

  /** A person opening or closing a turn's reasoning; from then on the block is theirs, not the answer's. */
  const toggleReasoning = useCallback((turnId: string, open: boolean) => {
    dispatch({ type: 'toggle_reasoning', turnId, open });
  }, []);

  const hydrate = useCallback(
    (detail: ConversationDetail) => {
      stop();
      conversationRef.current = detail.conversationId;
      focusRef.current = detail.focus ?? null;
      dispatch({
        type: 'hydrate',
        conversationId: detail.conversationId,
        turns: detail.turns,
        focus: detail.focus ?? null,
      });
    },
    [stop],
  );

  /** Chooses the account in focus, or clears it; it goes with the next message and starts nothing itself. */
  const setFocus = useCallback((focus: FocusAccount | null) => {
    focusRef.current = focus;
    dispatch({ type: 'set_focus', focus });
  }, []);

  /** What this conversation is still waiting on, asked for when it is opened. */
  const loadPending = useCallback(
    async (conversationId: string) => {
      try {
        const response = await fetch(`/api/conversations/${conversationId}/pending`, {
          headers: authHeaders(token),
        });
        if (!response.ok) return;
        const body = (await response.json()) as { pending: PendingProposal | null };
        if (!body.pending) return;
        dispatch({
          type: 'pending',
          confirmation: {
            callId: '',
            toolName: '',
            adjustmentId: body.pending.adjustmentId,
            adjustment: body.pending.adjustment,
            question: body.pending.question,
            state: '',
            expiresAt: body.pending.expiresAt,
          },
          expired: expired({ expiresAt: body.pending.expiresAt }),
        });
      } catch {
        // Nothing to show is the same as not knowing; the conversation still reads.
      }
    },
    [token],
  );

  return { state, send, cancel, reset, hydrate, answer, loadPending, toggleReasoning, setFocus };
}

/**
 * A caller's idempotency key for one answer to one proposal. It is derived rather than random, so a retry of the
 * same answer carries the same key — which is the whole point of having one.
 */
function keyFor(adjustmentId: string, approve: boolean): string {
  return `${adjustmentId}:${approve ? 'approve' : 'decline'}`;
}

/**
 * A terminal event that says the run was stopped: an aborted run as `@ag-ui/client` reports it, or the cancelled
 * outcome CopilotKit's runtime gives a run it stopped.
 */
function isStop(event: BaseEvent): boolean {
  if (event.type === 'RUN_ERROR') return (event as BaseEvent & { code?: string }).code === 'abort';
  return (event as BaseEvent & { outcome?: { type?: string } }).outcome?.type === 'cancelled';
}

/** What the run said, read as what became of the proposal. The words are the server's own. */
function outcomeOf(said: string, approve: boolean): 'applied' | 'declined' | 'gone' {
  if (/no longer waiting/i.test(said)) return 'gone';
  return approve ? 'applied' : 'declined';
}

/** What to say about a run that failed before it reached its agent, by the status the client carried. */
export function failureOf(error: unknown): [string, FailureKind] {
  return faceOf(statusOf(error));
}

/** The HTTP status a failed run reports, however the client carried it. */
function statusOf(error: unknown): number | undefined {
  if (error && typeof error === 'object') {
    const e = error as { status?: unknown; statusCode?: unknown; message?: unknown };
    if (typeof e.status === 'number') return e.status;
    if (typeof e.statusCode === 'number') return e.statusCode;
    const match = typeof e.message === 'string' ? /\b(4\d\d|5\d\d)\b/.exec(e.message) : null;
    if (match) return Number(match[1]);
  }
  return undefined;
}

/** Tells each plugin's run observer; one that throws is left out of this call and the chat goes on. */
function notify(observers: readonly PluginRunObserver[], call: (o: PluginRunObserver) => void) {
  for (const observer of observers) {
    try {
      call(observer);
    } catch {
      // A plugin's view of the run is its own; the run is unaffected.
    }
  }
}
