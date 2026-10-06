import { useInfiniteQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useApi, useUserKey } from '@maf/plugin-api';
import type { ConversationPage, RenameConversationRequest } from './types';

export const PAGE_SIZE = 30;
export const TITLE_MAX = 120;

/** The list's queries, by persona and search: switching persona never shows another user's list. */
export const CONVERSATIONS = 'conversations';
export const conversationsKey = (userKey: string, search: string) => [
  CONVERSATIONS,
  userKey,
  search,
];

export function useConversations(search: string) {
  const api = useApi();
  const userKey = useUserKey();
  return useInfiniteQuery({
    queryKey: conversationsKey(userKey, search),
    initialPageParam: null as string | null,
    queryFn: ({ pageParam, signal }) => {
      const params = new URLSearchParams({ limit: String(PAGE_SIZE) });
      if (search) params.set('search', search);
      if (pageParam) params.set('before', pageParam);
      return api<ConversationPage>(`/api/conversations?${params.toString()}`, { signal });
    },
    getNextPageParam: (last) => last.nextCursor ?? null,
  });
}

/** Validates a new title: 1–120 characters after trimming. Returns an error message or null. */
export function titleError(title: string): string | null {
  const trimmed = title.trim();
  if (!trimmed) return 'Title cannot be empty.';
  if (trimmed.length > TITLE_MAX) return `Title must be at most ${TITLE_MAX} characters.`;
  return null;
}

export function useRenameConversation() {
  const api = useApi();
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ id, title }: { id: string; title: string }) =>
      api<void>(`/api/conversations/${encodeURIComponent(id)}`, {
        method: 'PATCH',
        body: { title: title.trim() } satisfies RenameConversationRequest,
      }),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: [CONVERSATIONS] });
    },
  });
}

export function useDeleteConversation() {
  const api = useApi();
  const client = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      api<void>(`/api/conversations/${encodeURIComponent(id)}`, { method: 'DELETE' }),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: [CONVERSATIONS] });
    },
  });
}
