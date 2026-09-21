import { useCallback, useEffect, useReducer, useRef } from 'react';
import { authHeaders } from '../api/client';
import type { ConversationDetail, PendingProposal } from '../api/types';
import { useAuth } from '../auth/useAuth';
import { chatReducer, initialChatState } from './chatReducer';
import { readChatStream } from './readChatStream';
import { expired, type FailureKind } from './chatReducer';

let counter = 0;
const nextId = (prefix: string) =>
  `${prefix}-${Date.now().toString(36)}-${(counter++).toString(36)}`;

export function useChatStream() {
  const { session } = useAuth();
  const token = session?.token ?? null;
  const [state, dispatch] = useReducer(chatReducer, initialChatState);
  const abortRef = useRef<AbortController | null>(null);
  const conversationRef = useRef<string | undefined>(undefined);

  useEffect(() => {
    conversationRef.current = state.conversationId;
  }, [state.conversationId]);

  useEffect(() => () => abortRef.current?.abort(), []);

  const send = useCallback(
    async (message: string) => {
      const text = message.trim();
      if (!text) return;

      abortRef.current?.abort();
      const controller = new AbortController();
      abortRef.current = controller;

      dispatch({ type: 'send', userTurnId: nextId('u'), assistantTurnId: nextId('a'), text });

      const request = {
        threadId: conversationRef.current ?? null,
        runId: nextId('r'),
        messages: [{ id: nextId('u'), role: 'user', content: text }],
      };

      try {
        const response = await fetch('/api/chat', {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
            Accept: 'text/event-stream',
            ...authHeaders(token),
          },
          body: JSON.stringify(request),
          signal: controller.signal,
        });

        if (!response.ok || !response.body) {
          const [message, kind] = failure(response.status);
          dispatch({ type: 'stream_error', message, kind });
          return;
        }

        const sawDone = await readChatStream(response.body, (event) =>
          dispatch({ type: 'event', event }),
        );
        if (!sawDone) {
          dispatch({
            type: 'stream_error',
            message: 'The answer stopped part-way. Send it again.',
            kind: 'unavailable',
          });
        }
      } catch {
        if (controller.signal.aborted) return;
        dispatch({
          type: 'stream_error',
          message: 'The connection to the assistant was lost. Send it again.',
          kind: 'unavailable',
        });
      }
    },
    [token],
  );

  const reset = useCallback(() => {
    abortRef.current?.abort();
    conversationRef.current = undefined;
    dispatch({ type: 'reset' });
  }, []);

  /** Replaces the chat with a stored conversation; further messages continue it. */
  /**
   * A person's answer to a waiting write: a run that resumes the interrupt. The answer arrives in the
   * conversation like any other, and what became of the proposal is read from what the run said.
   */
  const answer = useCallback(
    async (adjustmentId: string, approve: boolean) => {
      const conversationId = conversationRef.current;
      if (!conversationId) return;

      const assistantTurnId = nextId('a');
      // Opens a streaming assistant turn so the monitor panel follows the answer run.
      dispatch({ type: 'start_answer', assistantTurnId });
      dispatch({ type: 'answering', adjustmentId });

      try {
        const response = await fetch('/api/chat', {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
            Accept: 'text/event-stream',
            ...authHeaders(token),
          },
          body: JSON.stringify({
            threadId: conversationId,
            runId: nextId('r'),
            messages: [],
            resume: [{ interruptId: adjustmentId, payload: { approve } }],
          }),
        });

        if (!response.ok || !response.body) {
          dispatch({
            type: 'stream_error',
            message: 'The assistant could not process the answer.',
            kind: 'unavailable',
          });
          dispatch({ type: 'answered', adjustmentId, outcome: 'gone' });
          return;
        }

        // Pipe events through the normal reducer pipeline so the monitor receives live trace events.
        // Also accumulate text deltas to determine the outcome.
        let said = '';
        const sawDone = await readChatStream(response.body, (event) => {
          dispatch({ type: 'event', event });
          if (event.type === 'text_delta') said += event.data.text;
        });
        if (!sawDone) {
          dispatch({
            type: 'stream_error',
            message: 'The answer stopped part-way. Try again.',
            kind: 'unavailable',
          });
        }
        dispatch({ type: 'answered', adjustmentId, outcome: outcomeOf(said, approve) });
      } catch {
        dispatch({
          type: 'stream_error',
          message: 'The connection was lost. Try again.',
          kind: 'unavailable',
        });
        dispatch({ type: 'answered', adjustmentId, outcome: 'gone' });
      }
    },
    [token],
  );

  const hydrate = useCallback((detail: ConversationDetail) => {
    abortRef.current?.abort();
    conversationRef.current = detail.conversationId;
    dispatch({ type: 'hydrate', conversationId: detail.conversationId, turns: detail.turns });
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

  return { state, send, reset, hydrate, answer, loadPending };
}

/** What the run said, read as what became of the proposal. The words are the server's own. */
function outcomeOf(said: string, approve: boolean): 'applied' | 'declined' | 'gone' {
  if (/no longer waiting/i.test(said)) return 'gone';
  return approve ? 'applied' : 'declined';
}

/**
 * What to say, and which of the three faces to say it with. A refusal says only that it was refused: why is
 * the server's business and telling a caller would be telling them about someone else's data.
 */
function failure(status: number): [string, FailureKind] {
  if (status === 401) return ['Not signed in — pick a dev persona.', 'refused'];
  if (status === 403 || status === 404) return ['That conversation is not available.', 'refused'];
  return ['The assistant is unavailable. Try again in a moment.', 'unavailable'];
}
