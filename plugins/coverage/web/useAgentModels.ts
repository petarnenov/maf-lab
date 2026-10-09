import { useQuery } from '@tanstack/react-query';
import type { AgentModels } from './types';
import { useApi } from '@maf/plugin-api';
import { coverageKeys } from './keys';

/** The allowlist, the run's limits and this file's estimate parts; the dialog reads the same query. */
export function useAgentModels(path: string) {
  const api = useApi();
  return useQuery({
    queryKey: coverageKeys.models(path),
    queryFn: ({ signal }) => api<AgentModels>(`/api/coverage/models?path=${encodeURIComponent(path)}`, { signal }),
  });
}
