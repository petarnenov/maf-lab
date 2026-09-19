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

export const sse = (event: string, data: unknown) =>
  `event: ${event}\ndata: ${JSON.stringify(data)}\n\n`;
