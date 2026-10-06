import { useQuery } from '@tanstack/react-query';
import type { ConversationDetail } from '../api/types';
import { useApi, useAuth } from '../auth/useAuth';

/** Query keys include the signed-in user, so switching persona never shows another user's history. */
export function useUserKey(): string {
  const { session } = useAuth();
  return session ? `${session.user.tenantId}/${session.user.userId}` : 'anonymous';
}

export const conversationKey = (userKey: string, id: string | undefined) => [
  'conversation',
  userKey,
  id,
];

/** A conversation reopened by its id: the core's (decision 5y); the list of them is the conversation-history plugin's. */
export function useConversation(id: string | undefined, enabled: boolean) {
  const api = useApi();
  const userKey = useUserKey();
  return useQuery({
    queryKey: conversationKey(userKey, id),
    queryFn: ({ signal }) =>
      api<ConversationDetail>(`/api/conversations/${encodeURIComponent(id ?? '')}`, { signal }),
    enabled: Boolean(id) && enabled,
    // Always load the current state of a conversation when it is opened.
    staleTime: 0,
    gcTime: 0,
  });
}
