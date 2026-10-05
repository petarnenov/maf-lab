import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import type { AdminJob, DriftReport, IndexStatus } from '../api/types';
import { useApi, useAuth } from '../auth/useAuth';
import styles from '../components/Page.module.css';
import { Progress } from '../components/Progress';
import { StopHint } from '../components/StopHint';
import { useEscToStop } from '../components/useEscToStop';
import { formatDate } from '../evals/format';
import { isJobActive } from './jobs';

export function IndexAdminPage() {
  const { session } = useAuth();
  const api = useApi();
  const queryClient = useQueryClient();
  const [jobId, setJobId] = useState<string | null>(null);
  const [targetModel, setTargetModel] = useState('');
  const token = session?.token;

  const status = useQuery({
    queryKey: ['admin', 'index', 'status', token],
    queryFn: ({ signal }) => api<IndexStatus>('/api/admin/index/status', { signal }),
  });
  const driftKey = ['admin', 'index', 'drift', token];
  const drift = useQuery({
    queryKey: driftKey,
    queryFn: ({ signal }) => api<DriftReport>('/api/admin/index/drift', { signal }),
  });

  const trackedJobId =
    jobId ?? (isJobActive(status.data?.currentJob) ? status.data!.currentJob!.jobId : null);
  const job = useQuery({
    queryKey: ['admin', 'jobs', trackedJobId, token],
    queryFn: ({ signal }) =>
      api<AdminJob>(`/api/admin/jobs/${encodeURIComponent(trackedJobId!)}`, { signal }),
    enabled: !!trackedJobId,
    refetchInterval: (query) => (isJobActive(query.state.data) || !query.state.data ? 1500 : false),
  });

  const jobActive = isJobActive(job.data);
  useEffect(() => {
    if (job.data && !isJobActive(job.data)) {
      void queryClient.invalidateQueries({ queryKey: ['admin', 'index'] });
    }
  }, [job.data, queryClient]);

  const runIndex = useMutation({
    mutationFn: () => api<AdminJob>('/api/admin/index/run', { method: 'POST' }),
    onSuccess: (started) => setJobId(started.jobId),
  });
  const migrate = useMutation({
    mutationFn: () =>
      api<AdminJob>('/api/admin/index/migrate', {
        method: 'POST',
        body: targetModel.trim() ? { targetModel: targetModel.trim() } : {},
      }),
    onSuccess: (started) => setJobId(started.jobId),
  });

  const busy = jobActive || runIndex.isPending || migrate.isPending;

  // Stopping (stop-anything): the job is asked through its cancel route, and the page says "Stopping…" until the job's
  // own state says it has ended; a drift report still loading is aborted, request and all.
  const [stoppingJob, setStoppingJob] = useState<string | null>(null);
  const [driftStopped, setDriftStopped] = useState(false);
  const cancelJob = useMutation({
    mutationFn: (id: string) =>
      api<AdminJob>(`/api/admin/jobs/${encodeURIComponent(id)}/cancel`, { method: 'POST' }),
    onSettled: () => void queryClient.invalidateQueries({ queryKey: ['admin', 'jobs'] }),
  });
  const stopping = jobActive && stoppingJob === job.data?.jobId;
  useEscToStop(jobActive || drift.isFetching, () => {
    if (jobActive && job.data && !stopping) {
      setStoppingJob(job.data.jobId);
      cancelJob.mutate(job.data.jobId);
    } else if (!jobActive && drift.isFetching) {
      setDriftStopped(true);
      void queryClient.cancelQueries({ queryKey: driftKey });
    }
  });

  return (
    <div className={styles.page}>
      <h1 className={styles.heading}>Index administration</h1>

      <div className={styles.cards}>
        <div className={styles.card}>
          <div className={styles.muted}>Drift (stale documents)</div>
          {/* With Neo4j down the graph half waits for its connect timeout (~5-8 s): progress-feedback. */}
          {drift.isLoading && (
            <>
              <Progress label="Checking the index and the graph…" />
              {!jobActive && <StopHint stopping={false} />}
            </>
          )}
          {driftStopped && !drift.data && !drift.isFetching && (
            <div className={styles.muted} data-testid="drift-stopped" role="status">
              Stopped.{' '}
              <button
                type="button"
                onClick={() => {
                  setDriftStopped(false);
                  void drift.refetch();
                }}
              >
                Check again
              </button>
            </div>
          )}
          {drift.isError && <div className={styles.error}>Unavailable</div>}
          {drift.data && (
            <>
              <div className={styles.big} data-testid="drift-percent">
                {drift.data.stalePercent.toFixed(1)}%
              </div>
              <div className={styles.muted}>
                {drift.data.staleDocuments} of {drift.data.totalDocuments} documents
              </div>
              {drift.data.graph && (
                <div className={styles.muted} data-testid="drift-graph">
                  {drift.data.graph.available
                    ? `Graph: ${drift.data.graph.outOfSync} of ${drift.data.totalDocuments} out of sync`
                    : 'Graph: unavailable'}
                </div>
              )}
            </>
          )}
        </div>
        <div className={styles.card}>
          <div className={styles.muted}>Active dense vector</div>
          <div className={styles.big}>{status.data?.activeDenseVector ?? '—'}</div>
        </div>
      </div>

      <div className={styles.actions}>
        <button type="button" disabled={busy} onClick={() => runIndex.mutate()}>
          Run indexing
        </button>
        <input
          type="text"
          aria-label="Target model"
          placeholder="Target model (optional)"
          value={targetModel}
          onChange={(e) => setTargetModel(e.target.value)}
        />
        <button type="button" disabled={busy} onClick={() => migrate.mutate()}>
          Run migration
        </button>
        {(runIndex.isError || migrate.isError) && (
          <span className={styles.error} role="alert">
            Could not start the job.
          </span>
        )}
      </div>

      {job.data && (
        <p data-testid="job-status">
          Job <code>{job.data.kind}</code>: <strong>{job.data.state}</strong>
          {job.data.summary ? ` — ${job.data.summary}` : ''}
        </p>
      )}
      {jobActive && (
        <>
          <Progress
            label={stopping ? 'Stopping the job…' : `Running ${job.data?.kind ?? 'the job'}…`}
          />
          <StopHint stopping={stopping} />
        </>
      )}

      <h2 className={styles.subheading}>Chunks by model_version</h2>
      {status.isError && <p className={styles.error}>Could not load index status.</p>}
      {status.data && (
        <table className={styles.table}>
          <thead>
            <tr>
              <th>model_version</th>
              <th>Chunks</th>
            </tr>
          </thead>
          <tbody>
            {status.data.modelVersions.map((mv) => (
              <tr key={mv.modelVersion}>
                <td className={styles.mono}>{mv.modelVersion}</td>
                <td>{mv.chunks}</td>
              </tr>
            ))}
            {status.data.modelVersions.length === 0 && (
              <tr>
                <td colSpan={2} className={styles.muted}>
                  Index is empty.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      )}

      {drift.data && (drift.data.stale.length > 0 || drift.data.missingFromIndex.length > 0) && (
        <>
          <h2 className={styles.subheading}>Stale documents</h2>
          <table className={styles.table}>
            <thead>
              <tr>
                <th>Source</th>
                <th>Source updated</th>
                <th>Indexed</th>
              </tr>
            </thead>
            <tbody>
              {drift.data.stale.map((doc) => (
                <tr key={doc.docId}>
                  <td className={styles.mono}>{doc.sourcePath}</td>
                  <td>{formatDate(doc.sourceUpdatedAt)}</td>
                  <td>{formatDate(doc.indexedUpdatedAt)}</td>
                </tr>
              ))}
              {drift.data.missingFromIndex.map((path) => (
                <tr key={path}>
                  <td className={styles.mono}>{path}</td>
                  <td colSpan={2} className={styles.muted}>
                    not indexed
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </>
      )}
    </div>
  );
}
