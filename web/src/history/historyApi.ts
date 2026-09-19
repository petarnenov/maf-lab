import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { ConversationDetail, ConversationPage, RenameConversationRequest } from '../api/types';
import { useApi, useAuth } from '../auth/useAuth';

export const PAGE_SIZE = 30;
export const TITLE_MAX = 120;

/** Query keys include the signed-in user, so switching persona never shows another user's history. */
export function useUserKey(): string {
  const { session } = useAuth();
  return session ? `${session.user.firmId}/${session.user.userId}` : 'anonymous';
}

export const conversationsKey = (userKey: string, search: string) => [
  'conversations',
  userKey,
  search,
];
export const conversationKey = (userKey: string, id: string | undefined) => [
  'conversation',
  userKey,
  id,
];

export function useConversations(search: string) {
  const api = useApi();
  const userKey = useUserKey();
  return useInfiniteQuery({
    queryKey: conversationsKey(userKey, search),
    initialPageParam: null as string | null,
    queryFn: ({ pageParam }) => {
      const params = new URLSearchParams({ limit: String(PAGE_SIZE) });
      if (search) params.set('search', search);
      if (pageParam) params.set('before', pageParam);
      return api<ConversationPage>(`/api/conversations?${params.toString()}`);
    },
    getNextPageParam: (last) => last.nextCursor ?? null,
  });
}

export function useConversation(id: string | undefined, enabled: boolean) {
  const api = useApi();
  const userKey = useUserKey();
  return useQuery({
    queryKey: conversationKey(userKey, id),
    queryFn: () => api<ConversationDetail>(`/api/conversations/${encodeURIComponent(id ?? '')}`),
    enabled: Boolean(id) && enabled,
    // Always load the current state of a conversation when it is opened.
    staleTime: 0,
    gcTime: 0,
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
      void client.invalidateQueries({ queryKey: ['conversations'] });
      void client.invalidateQueries({ queryKey: ['conversation'] });
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
      void client.invalidateQueries({ queryKey: ['conversations'] });
    },
  });
}
