import { useQuery } from '@tanstack/react-query';
import type { AguiFrame, TraceEvent, TurnTraceDocument } from './types';
import { ApiError, useApi } from '@maf/plugin-api';

/** Stored trace of a finished turn (GET /api/turns/{turnId}/trace). */
export function useTurnTrace(
  turnId: string | undefined,
  options: { enabled?: boolean; placeholder?: TraceEvent[]; placeholderFrames?: AguiFrame[] } = {},
) {
  const api = useApi();
  const placeholder = options.placeholder;
  return useQuery({
    queryKey: ['turn-trace', turnId],
    queryFn: ({ signal }) =>
      api<TurnTraceDocument>(`/api/turns/${encodeURIComponent(turnId ?? '')}/trace`, { signal }),
    enabled: Boolean(turnId) && (options.enabled ?? true),
    staleTime: 60_000,
    // A trace the monitor no longer keeps (404) stays gone; reading again would say the same.
    retry: (failures, error) =>
      !(error instanceof ApiError && error.status === 404) && failures < 1,
    placeholderData:
      placeholder && placeholder.length > 0 && turnId
        ? {
            turnId,
            conversationId: '',
            createdAt: '',
            events: placeholder,
            aguiFrames: options.placeholderFrames ?? null,
          }
        : undefined,
  });
}

/** Whether a stored trace failed because the monitor no longer keeps it (or never kept it). */
export function isNoTrace(error: unknown): boolean {
  return error instanceof ApiError && error.status === 404;
}
