import { useQuery } from '@tanstack/react-query';
import type { TestAgentOverview } from '../api/types';
import { useApi, useAuth } from '../auth/useAuth';

/** Under the page's ['admin', 'a2a'] prefix, so the page's Refresh and a cancel ask for it again. */
export const testAgentKey = (token: string | undefined) =>
  ['coverage', 'test-agent', token] as const;

/** The test agent overview, from the api only; the page and the section share it (one request). */
export function useTestAgentOverview() {
  const { session } = useAuth();
  const api = useApi();
  return useQuery({
    queryKey: testAgentKey(session?.token),
    queryFn: ({ signal }) => api<TestAgentOverview>('/api/admin/coverage/test-agent', { signal }),
  });
}
