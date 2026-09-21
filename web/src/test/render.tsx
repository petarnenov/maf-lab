import { EventType } from '@ag-ui/core';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render } from '@testing-library/react';
import type { ReactElement } from 'react';
import { MemoryRouter } from 'react-router';
import type { Role } from '../api/types';
import { AuthProvider } from '../auth/AuthProvider';
import type { Session } from '../auth/session';

export function makeSession(role: Role = 'ADVISOR', firmId = 'firm-a'): Session {
  return {
    token: `token-${role}`,
    expiresAt: new Date(Date.now() + 3600_000).toISOString(),
    user: { userId: `u-${role.toLowerCase()}`, firmId, role, advisorIds: [], label: role },
  };
}

export function renderWithProviders(
  ui: ReactElement,
  { session = makeSession(), route = '/' }: { session?: Session | null; route?: string } = {},
) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <AuthProvider initialSession={session}>
        <MemoryRouter initialEntries={[route]}>{ui}</MemoryRouter>
      </AuthProvider>
    </QueryClientProvider>,
  );
}

export function jsonResponse(body: unknown, status = 200): Response {
  return new Response(body === undefined ? null : JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

/** Builds a Response whose body streams the given text chunks, as the SSE endpoint does. */
export function streamResponse(chunks: string[], status = 200): Response {
  const encoder = new TextEncoder();
  const body = new ReadableStream<Uint8Array>({
    start(controller) {
      for (const chunk of chunks) controller.enqueue(encoder.encode(chunk));
      controller.close();
    },
  });
  return new Response(body, { status, headers: { 'Content-Type': 'text/event-stream' } });
}

export const sse = (event: EventType, data: Record<string, unknown>) =>
  `event: ${event}\ndata: ${JSON.stringify({ type: event, ...data })}\n\n`;

/**
 * A run as the protocol streams it. Tests say what happened; this says it in AG-UI, so the shape of the wire
 * lives in one place rather than in every fixture.
 */
export const run = {
  started: (threadId = 'conv-1', runId = 'r1') => sse(EventType.RUN_STARTED, { threadId, runId }),

  text: (text: string, messageId = 'm1') => [
    sse(EventType.TEXT_MESSAGE_START, { messageId, role: 'assistant' }),
    sse(EventType.TEXT_MESSAGE_CONTENT, { messageId, delta: text }),
    sse(EventType.TEXT_MESSAGE_END, { messageId }),
  ],

  /** Just the content of a message already open — for tests about chunking. */
  delta: (text: string, messageId = 'm1') =>
    sse(EventType.TEXT_MESSAGE_CONTENT, { messageId, delta: text }),

  toolCall: (toolCallId: string, toolCallName: string, args = '') => [
    sse(EventType.TOOL_CALL_START, { toolCallId, toolCallName }),
    sse(EventType.TOOL_CALL_ARGS, { toolCallId, delta: args }),
    sse(EventType.TOOL_CALL_END, { toolCallId }),
  ],

  toolResult: (toolCallId: string, summary: string, tool = '', sourceCount = 0, isError = false) =>
    sse(EventType.TOOL_CALL_RESULT, {
      toolCallId,
      messageId: toolCallId,
      content: JSON.stringify({ tool, summary, sourceCount, isError }),
    }),

  sources: (sources: unknown[]) =>
    sse(EventType.CUSTOM, { name: 'maf-lab/sources', value: { sources } }),

  trace: (event: unknown) => sse(EventType.CUSTOM, { name: 'maf-lab/trace', value: event }),

  done: (threadId = 'conv-1', turnId = 't1') =>
    sse(EventType.RUN_FINISHED, {
      threadId,
      runId: 'r1',
      outcome: { type: 'success' },
      result: { turnId },
    }),

  error: (message: string) => sse(EventType.RUN_ERROR, { message }),
};
