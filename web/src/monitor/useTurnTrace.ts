import { useQuery } from '@tanstack/react-query';
import type { AguiFrame, TraceEvent, TurnTraceDocument } from '../api/types';
import { useApi } from '../auth/useAuth';

/** Stored trace of a finished turn (GET /api/turns/{turnId}/trace). */
export function useTurnTrace(
  turnId: string | undefined,
  options: { enabled?: boolean; placeholder?: TraceEvent[]; placeholderFrames?: AguiFrame[] } = {},
) {
  const api = useApi();
  const placeholder = options.placeholder;
  return useQuery({
    queryKey: ['turn-trace', turnId],
    queryFn: () => api<TurnTraceDocument>(`/api/turns/${encodeURIComponent(turnId ?? '')}/trace`),
    enabled: Boolean(turnId) && (options.enabled ?? true),
    staleTime: 60_000,
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
