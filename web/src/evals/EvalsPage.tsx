import { useQuery } from '@tanstack/react-query';
import { Fragment, useState } from 'react';
import type { EvalReport, EvalReportSummary } from '../api/types';
import { useApi, useAuth } from '../auth/useAuth';
import styles from '../components/Page.module.css';
import { formatDate, formatMetric } from './format';

export function EvalsPage() {
  const { session } = useAuth();
  const api = useApi();
  const [selected, setSelected] = useState<string | null>(null);

  const reports = useQuery({
    queryKey: ['evals', 'reports', session?.token],
    queryFn: () => api<EvalReportSummary[]>('/api/evals/reports'),
    enabled: !!session,
  });

  if (!session)
    return <p className={styles.notice}>Pick a dev persona in the header to continue.</p>;

  return (
    <div className={styles.page}>
      <h1 className={styles.heading}>Eval runs</h1>
      {reports.isLoading && <p className={styles.muted}>Loading reports…</p>}
      {reports.isError && (
        <p className={styles.error} role="alert">
          Could not load eval reports.
        </p>
      )}
      {reports.data && reports.data.length === 0 && (
        <p className={styles.muted}>
          No eval reports yet. Run <code>dotnet run --project src/Maf.Lab.Eval -- --suite all</code>
          .
        </p>
      )}
      {reports.data && reports.data.length > 0 && (
        <table className={styles.table}>
          <thead>
            <tr>
              <th>Date</th>
              <th>Suite</th>
              <th>Variant</th>
              <th>Metrics</th>
              <th>Result</th>
            </tr>
          </thead>
          <tbody>
            {reports.data.map((run) => (
              <Fragment key={run.runId}>
                {(run.variants.length > 0 ? run.variants : [null]).map((variant, index) => (
                  <tr
                    key={`${run.runId}-${variant?.name ?? 'none'}`}
                    className={`${styles.clickable} ${selected === run.runId ? styles.selected : ''}`}
                    onClick={() => setSelected(run.runId)}
                  >
                    {index === 0 && (
                      <>
                        <td rowSpan={Math.max(run.variants.length, 1)}>
                          {formatDate(run.startedAt)}
                        </td>
                        <td rowSpan={Math.max(run.variants.length, 1)}>{run.suite}</td>
                      </>
                    )}
                    <td>{variant?.name ?? '—'}</td>
                    <td className={styles.mono}>
                      {variant
                        ? Object.entries(variant.metrics)
                            .map(([k, v]) => `${k}=${formatMetric(v)}`)
                            .join('  ')
                        : '—'}
                    </td>
                    <td>
                      <PassFail passed={variant ? variant.passed : run.passed} />
                    </td>
                  </tr>
                ))}
              </Fragment>
            ))}
          </tbody>
        </table>
      )}
      {selected && <ReportDetail runId={selected} />}
    </div>
  );
}

function PassFail({ passed }: { passed: boolean }) {
  return <span className={passed ? styles.pass : styles.fail}>{passed ? 'pass' : 'fail'}</span>;
}

function ReportDetail({ runId }: { runId: string }) {
  const { session } = useAuth();
  const api = useApi();
  const report = useQuery({
    queryKey: ['evals', 'report', runId, session?.token],
    queryFn: () => api<EvalReport>(`/api/evals/reports/${encodeURIComponent(runId)}`),
  });

  if (report.isLoading) return <p className={styles.muted}>Loading report…</p>;
  if (report.isError || !report.data) {
    return (
      <p className={styles.error} role="alert">
        Could not load report {runId}.
      </p>
    );
  }

  const r = report.data;
  return (
    <section aria-label="Report detail">
      <h2 className={styles.subheading}>
        {r.suite} · {formatDate(r.startedAt)} · <PassFail passed={r.passed} />
      </h2>
      {Object.keys(r.settings).length > 0 && (
        <p className={styles.mono}>
          {Object.entries(r.settings)
            .map(([k, v]) => `${k}=${v}`)
            .join('  ')}
        </p>
      )}
      {r.variants.map((variant) => (
        <div key={variant.name}>
          <h3 className={styles.subheading}>
            {variant.name} ({variant.cases} cases) · <PassFail passed={variant.passed} />
          </h3>
          <table className={styles.table}>
            <thead>
              <tr>
                <th>Metric</th>
                <th>Value</th>
                <th>Threshold</th>
              </tr>
            </thead>
            <tbody>
              {Object.entries(variant.metrics).map(([name, value]) => {
                const threshold = variant.thresholds[name];
                const below = threshold !== undefined && value < threshold;
                return (
                  <tr key={name}>
                    <td>{name}</td>
                    <td className={below ? styles.fail : undefined}>{formatMetric(value)}</td>
                    <td>{threshold !== undefined ? formatMetric(threshold) : '—'}</td>
                  </tr>
                );
              })}
            </tbody>
          </table>
          {variant.failures.length > 0 && (
            <details>
              <summary>{variant.failures.length} failing cases</summary>
              <ul>
                {variant.failures.map((f) => (
                  <li key={f.caseId}>
                    <code>{f.caseId}</code>: {f.reason}
                  </li>
                ))}
              </ul>
            </details>
          )}
        </div>
      ))}
    </section>
  );
}
