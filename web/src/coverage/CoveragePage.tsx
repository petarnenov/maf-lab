import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';
import { useSearchParams } from 'react-router';
import type { AdminJob, CoverageFileDetail, CoverageTree as Tree } from '../api/types';
import { useApi, useAuth } from '../auth/useAuth';
import page from '../components/Page.module.css';
import { CoverageTree } from './CoverageTree';
import { FileView } from './FileView';
import { coverageKeys } from './keys';
import styles from './CoveragePage.module.css';

/** How well the repository's own source is covered by its tests, file by file and line by line. */
export function CoveragePage() {
  const { session } = useAuth();
  const api = useApi();
  const [params, setParams] = useSearchParams();
  const selected = params.get('file');
  const isAdmin = session?.user.role === 'FIRM_ADMIN';

  const tree = useQuery({
    queryKey: coverageKeys.tree,
    queryFn: () => api<Tree>('/api/coverage/tree'),
    enabled: !!session,
  });

  const file = useQuery({
    queryKey: coverageKeys.file(selected ?? ''),
    queryFn: () => api<CoverageFileDetail>(`/api/coverage/files?path=${encodeURIComponent(selected!)}`),
    enabled: !!session && !!selected,
  });

  if (!session) return <p className={page.notice}>Pick a dev persona in the header to continue.</p>;

  const select = (path: string) => setParams({ file: path });

  return (
    <div className={styles.page}>
      <div className={styles.titleRow}>
        <h1 className={page.heading}>Coverage</h1>
        {isAdmin && tree.data?.hasSnapshot && <RefreshControl />}
      </div>
      <p className={page.muted}>
        Line coverage of the repository's C# and TypeScript source, from the latest measurement of each file.
      </p>

      {tree.isLoading && <p className={page.muted}>Loading coverage…</p>}
      {tree.isError && (
        <p className={page.error} role="alert">
          Could not load coverage.
        </p>
      )}
      {tree.data && !tree.data.hasSnapshot && (
        <div className={styles.empty}>
          <p>No coverage report yet.</p>
          {isAdmin ? (
            <RefreshControl />
          ) : (
            <p className={page.muted}>An administrator can measure it.</p>
          )}
        </div>
      )}
      {tree.data?.hasSnapshot && (
        <div className={styles.split}>
          <CoverageTree tree={tree.data} selected={selected} onSelect={select} />
          <div className={styles.fileSlot}>
            {!selected && <p className={page.muted}>Pick a file to see its lines.</p>}
            {selected && file.isLoading && <p className={page.muted}>Loading the file…</p>}
            {selected && file.isError && (
              <p className={page.error} role="alert">
                Could not load this file.
              </p>
            )}
            {selected && file.data && <FileView detail={file.data} />}
          </div>
        </div>
      )}
    </div>
  );
}

/** Measures both toolchains at main. One refresh runs at a time; a second press shows the one running. */
function RefreshControl() {
  const api = useApi();
  const client = useQueryClient();
  const current = useQuery({
    queryKey: coverageKeys.refresh,
    queryFn: () => api<AdminJob | undefined>('/api/coverage/refresh'),
    refetchInterval: (q) => (q.state.data?.state === 'running' ? 2000 : false),
  });
  const start = useMutation({
    mutationFn: () => api<AdminJob>('/api/coverage/refresh', { method: 'POST' }),
    onSuccess: (job) => client.setQueryData(coverageKeys.refresh, job),
  });

  const job = current.data;
  const running = job?.state === 'running';
  const finishedAt = job?.finishedAt;
  useEffect(() => {
    if (finishedAt) void client.invalidateQueries({ queryKey: coverageKeys.all });
  }, [finishedAt, client]);

  return (
    <div className={styles.refresh}>
      <button type="button" onClick={() => start.mutate()} disabled={running || start.isPending}>
        {running ? 'Measuring…' : 'Refresh coverage'}
      </button>
      {job?.state === 'failed' && (
        <span className={page.error} role="alert">
          The last refresh failed.
        </span>
      )}
      {start.isError && (
        <span className={page.error} role="alert">
          Could not start a refresh.
        </span>
      )}
    </div>
  );
}
