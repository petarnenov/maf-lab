import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import type { AdminJob, DriftReport, IndexStatus } from '../api/types';
import { useApi, useAuth } from '../auth/useAuth';
import styles from '../components/Page.module.css';
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
    queryFn: () => api<IndexStatus>('/api/admin/index/status'),
  });
  const drift = useQuery({
    queryKey: ['admin', 'index', 'drift', token],
    queryFn: () => api<DriftReport>('/api/admin/index/drift'),
  });

  const trackedJobId =
    jobId ?? (isJobActive(status.data?.currentJob) ? status.data!.currentJob!.jobId : null);
  const job = useQuery({
    queryKey: ['admin', 'jobs', trackedJobId, token],
    queryFn: () => api<AdminJob>(`/api/admin/jobs/${encodeURIComponent(trackedJobId!)}`),
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

  return (
    <div className={styles.page}>
      <h1 className={styles.heading}>Index administration</h1>

      <div className={styles.cards}>
        <div className={styles.card}>
          <div className={styles.muted}>Drift (stale documents)</div>
          {drift.isLoading && <div className={styles.muted}>Loading…</div>}
          {drift.isError && <div className={styles.error}>Unavailable</div>}
          {drift.data && (
            <>
              <div className={styles.big} data-testid="drift-percent">
                {drift.data.stalePercent.toFixed(1)}%
              </div>
              <div className={styles.muted}>
                {drift.data.staleDocuments} of {drift.data.totalDocuments} documents
              </div>
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
