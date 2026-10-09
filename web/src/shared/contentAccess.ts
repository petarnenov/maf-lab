import { useQuery } from '@tanstack/react-query';
import { useApi, useAuth, useUserKey } from '../plugins/api';

export interface ContentAccessGrant {
  id: number;
  operatorId: string;
  reason: string;
  startedAt: string;
  expiresAt: string;
  endRequestedAt: string | null;
  endedAt: string | null;
}

export interface ContentAccessState {
  grant: ContentAccessGrant | null;
  active: boolean;
}

export const CONTENT_ACCESS_PATH = '/api/platform/content-access';

/** The server's acknowledgement controls the banner; the browser never infers completion from expiry. */
export function useContentAccess() {
  const api = useApi();
  const { session } = useAuth();
  const user = useUserKey();
  const key = ['content-access', user, session?.token];
  const query = useQuery({
    queryKey: key,
    queryFn: ({ signal }) => api<ContentAccessState>(CONTENT_ACCESS_PATH, { signal }),
    enabled: session?.user.role === 'PLATFORM_ADMIN',
    refetchInterval: 5000,
  });
  return { query, key, api };
}
