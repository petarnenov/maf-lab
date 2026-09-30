import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';
import { useSearchParams } from 'react-router';
import { ApiError } from '../api/client';
import type { AdminJob, CoverageFileDetail, CoverageTree as Tree, RunSummary } from '../api/types';
import { useApi, useAuth } from '../auth/useAuth';
import page from '../components/Page.module.css';
import { CandidatePanel } from './CandidatePanel';
import { CoverageTree } from './CoverageTree';
import { FileView } from './FileView';
import { when } from './format';
import { coverageKeys } from './keys';
import { RunActivityButton } from './RunActivity';
import { RunStatus } from './RunStatus';
import { agentHas } from './treeRuns';
import { useRunEvents } from './useRunEvents';
import { ThresholdControl } from './ThresholdControl';
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
    // Rows beyond the few followed live still move while the agent works.
    refetchInterval: (q) => (q.state.data?.files.some((f) => agentHas(f.run)) ? 10_000 : false),
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
        {isAdmin && tree.data?.hasSnapshot && <RefreshControl latest={latestMeasurement(tree.data)} />}
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
            {selected && file.isError && <FileError error={file.error} canRefresh={isAdmin} />}
            {selected && file.data && (
              <FileView detail={file.data}>
                {file.data.run?.state === 'candidate' && (
                  <CandidatePanel run={file.data.run} canDecide={isAdmin} />
                )}
                {isAdmin ? (
                  <ThresholdControl
                    key={`${file.data.path}:${file.data.summary.threshold}`}
                    detail={file.data}
                    defaultThreshold={tree.data.defaultThresholdPct}
                  />
                ) : (
                  file.data.run && <LiveRunStatus run={file.data.run} />
                )}
              </FileView>
            )}
          </div>
        </div>
      )}
    </div>
  );
}

/**
 * Why a file cannot be shown. A 409 is a measurement at a commit this repository does not have (source_unavailable):
 * the server's message says which, and a refresh at main heals it. Anything else stays generic.
 */
function FileError({ error, canRefresh }: { error: Error; canRefresh: boolean }) {
  if (error instanceof ApiError && error.status === 409) {
    return (
      <div role="alert">
        <p className={page.error}>{error.message}</p>
        {canRefresh && <RefreshControl />}
      </div>
    );
  }
  return (
    <p className={page.error} role="alert">
      Could not load this file.
    </p>
  );
}

/** Measures both toolchains at main. One refresh runs at a time; a second press shows the one running. */
function RefreshControl({ latest }: { latest?: string | null }) {
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
          The last refresh failed{job.finishedAt ? ` at ${when(job.finishedAt)}` : ''}
          {job.summary ? `: ${job.summary}` : '.'}
          {latest && <span className={page.muted}> Current coverage was measured at {when(latest)}.</span>}
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

/** A non-admin sees a run move too, without its controls. */
function LiveRunStatus({ run }: { run: RunSummary }) {
  const live = useRunEvents(run);
  return live ? (
    <div className={styles.thresholdControl}>
      <RunStatus run={live} />
      <RunActivityButton run={live} canCancel={false} />
    </div>
  ) : null;
}

/** When the newest measurement on screen was taken: what a failed refresh left in place. */
function latestMeasurement(tree: Tree): string | null {
  return tree.files.reduce<string | null>((latest, f) => (!latest || f.measuredAt > latest ? f.measuredAt : latest), null);
}
