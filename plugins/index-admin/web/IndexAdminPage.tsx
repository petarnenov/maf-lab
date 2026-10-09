import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { type AdminJob, useApi, useUserKey } from '@maf/plugin-api';
import styles from '@maf/shared/Page.module.css';
import { Progress } from '@maf/shared/Progress';
import { StopHint } from '@maf/shared/StopHint';
import { useEscToStop } from '@maf/shared/useEscToStop';
import { formatDate } from '@maf/shared/format';
import { isJobActive } from './jobs';
import type { CorpusView, DriftReport, IndexStatus } from './types';

/** Graph drift as the card says it: in sync or not, unreachable, or no graph built for this corpus. */
function graphLine(drift: DriftReport): string | null {
  const graph = drift.graph;
  if (!graph) return null;
  if (graph.available) return `Graph: ${graph.outOfSync} of ${drift.totalDocuments} out of sync`;
  return graph.reason === 'not-built' ? 'Graph: not built for this corpus' : 'Graph: unavailable';
}

export function IndexAdminPage() {
  const api = useApi();
  const userKey = useUserKey();
  const queryClient = useQueryClient();
  const [jobId, setJobId] = useState<string | null>(null);
  const [targetModel, setTargetModel] = useState('');
  const [chosen, setChosen] = useState<string | null>(null);

  // The corpora the installed plugins declare; the first is shown until the admin picks another.
  const corpora = useQuery({
    queryKey: ['admin', 'index', 'corpora', userKey],
    queryFn: ({ signal }) => api<CorpusView[]>('/api/platform/index/corpora', { signal }),
  });
  const corpus = chosen ?? corpora.data?.[0]?.name ?? null;
  const query = corpus ? `?corpus=${encodeURIComponent(corpus)}` : '';

  const status = useQuery({
    queryKey: ['admin', 'index', 'status', corpus, userKey],
    queryFn: ({ signal }) => api<IndexStatus>(`/api/platform/index/status${query}`, { signal }),
    enabled: !!corpus,
  });
  const driftKey = ['admin', 'index', 'drift', corpus, userKey];
  const drift = useQuery({
    queryKey: driftKey,
    queryFn: ({ signal }) => api<DriftReport>(`/api/platform/index/drift${query}`, { signal }),
    enabled: !!corpus,
  });

  const trackedJobId =
    jobId ?? (isJobActive(status.data?.currentJob) ? status.data!.currentJob!.jobId : null);
  const job = useQuery({
    queryKey: ['admin', 'jobs', trackedJobId, userKey],
    queryFn: ({ signal }) =>
      api<AdminJob>(`/api/platform/jobs/${encodeURIComponent(trackedJobId!)}`, { signal }),
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
    mutationFn: () =>
      api<AdminJob>('/api/platform/index/run', { method: 'POST', body: { corpus } }),
    onSuccess: (started) => setJobId(started.jobId),
  });
  const migrate = useMutation({
    mutationFn: () =>
      api<AdminJob>('/api/platform/index/migrate', {
        method: 'POST',
        body: targetModel.trim() ? { corpus, targetModel: targetModel.trim() } : { corpus },
      }),
    onSuccess: (started) => setJobId(started.jobId),
  });

  const busy = !corpus || jobActive || runIndex.isPending || migrate.isPending;

  // Stopping (stop-anything): the job is asked through its cancel route, and the page says "Stopping…" until the job's
  // own state says it has ended; a drift report still loading is aborted, request and all.
  const [stoppingJob, setStoppingJob] = useState<string | null>(null);
  const [driftStopped, setDriftStopped] = useState(false);
  const cancelJob = useMutation({
    mutationFn: (id: string) =>
      api<AdminJob>(`/api/platform/jobs/${encodeURIComponent(id)}/cancel`, { method: 'POST' }),
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

      {corpora.isError && <p className={styles.error}>Could not load the corpora.</p>}
      {corpora.data?.length === 0 && (
        <p className={styles.muted} data-testid="no-corpus">
          No installed plugin declares a corpus.
        </p>
      )}
      {corpora.data && corpora.data.length > 1 && (
        <label className={styles.muted}>
          Corpus{' '}
          <select
            aria-label="Corpus"
            value={corpus ?? ''}
            disabled={jobActive}
            onChange={(e) => {
              setChosen(e.target.value);
              setDriftStopped(false);
            }}
          >
            {corpora.data.map((c) => (
              <option key={c.name} value={c.name}>
                {c.name}
              </option>
            ))}
          </select>
        </label>
      )}
      {corpora.data?.length === 1 && (
        <p className={styles.muted} data-testid="corpus">
          Corpus: {corpus}
        </p>
      )}

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
              {graphLine(drift.data) && (
                <div className={styles.muted} data-testid="drift-graph">
                  {graphLine(drift.data)}
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
