import { useCallback, useEffect, useReducer, useRef } from 'react';
import { authHeaders } from '../api/client';
import type { ChatRequest } from '../api/types';
import { useAuth } from '../auth/useAuth';
import { chatReducer, initialChatState } from './chatReducer';
import { readChatStream } from './readChatStream';

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

      const request: ChatRequest = { message: text };
      if (conversationRef.current) request.conversationId = conversationRef.current;

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
          dispatch({ type: 'stream_error', message: failureMessage(response.status) });
          return;
        }

        const sawDone = await readChatStream(response.body, (event) =>
          dispatch({ type: 'event', event }),
        );
        if (!sawDone) {
          dispatch({ type: 'stream_error', message: 'The answer stream ended unexpectedly.' });
        }
      } catch (error) {
        if (controller.signal.aborted) return;
        dispatch({
          type: 'stream_error',
          message: error instanceof Error ? 'Connection lost.' : 'Unexpected error.',
        });
      }
    },
    [token],
  );

  const reset = useCallback(() => {
    abortRef.current?.abort();
    dispatch({ type: 'reset' });
  }, []);

  return { state, send, reset };
}

function failureMessage(status: number): string {
  if (status === 401) return 'Not signed in — pick a dev persona.';
  if (status === 404) return 'Conversation not found.';
  return `The assistant is unavailable (${status}).`;
}
