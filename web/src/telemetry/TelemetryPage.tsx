import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import type { TelemetryPanel, TelemetryReport } from '../api/types';
import { useApi, useAuth } from '../auth/useAuth';
import styles from '../components/Page.module.css';
import panel from './TelemetryPage.module.css';

/** The windows the server will answer for. Asking for anything else is refused there, not here. */
const WINDOWS: { id: string; label: string }[] = [
  { id: '15m', label: 'Last 15 minutes' },
  { id: '1h', label: 'Last hour' },
  { id: '6h', label: 'Last 6 hours' },
  { id: '24h', label: 'Last 24 hours' },
];

/** What the stack has measured about itself. Every number says which period it covers. */
export function TelemetryPage() {
  const { session } = useAuth();
  const api = useApi();
  const [window, setWindow] = useState('1h');

  const report = useQuery({
    queryKey: ['telemetry', window, session?.token],
    queryFn: () => api<TelemetryReport>(`/api/telemetry?window=${window}`),
    enabled: !!session,
    refetchInterval: 15_000,
  });

  if (!session)
    return <p className={styles.notice}>Pick a dev persona in the header to continue.</p>;

  const period = WINDOWS.find((w) => w.id === window)?.label ?? window;

  return (
    <div className={styles.page}>
      <h1 className={styles.heading}>Telemetry</h1>
      <p className={styles.subheading}>
        What the stack measured about itself, from its own traces and metrics. No message content is
        here, because none of it is in the signals this reads.
      </p>

      <div className={panel.controls}>
        <label>
          Period{' '}
          <select aria-label="Period" value={window} onChange={(e) => setWindow(e.target.value)}>
            {WINDOWS.map((w) => (
              <option key={w.id} value={w.id}>
                {w.label}
              </option>
            ))}
          </select>
        </label>
        {report.data?.traceUrl && (
          <a
            className={panel.traceLink}
            href={report.data.traceUrl}
            target="_blank"
            rel="noreferrer"
          >
            Open the trace store
          </a>
        )}
      </div>

      {report.isLoading && <p className={styles.muted}>Loading the numbers…</p>}
      {report.isError && (
        <p className={styles.error} role="alert">
          Could not read the numbers.
        </p>
      )}
      {report.data && !report.data.available && (
        <p className={styles.error} role="alert">
          {report.data.reason ?? 'The numbers are unavailable.'}
        </p>
      )}

      {report.data?.available && (
        <div className={panel.panels}>
          {report.data.panels.map((p) => (
            <Panel key={p.id} panel={p} period={period} />
          ))}
        </div>
      )}
    </div>
  );
}

function Panel({ panel: data, period }: { panel: TelemetryPanel; period: string }) {
  const measured = data.series.filter((s) => s.value != null);
  const max = Math.max(1, ...measured.map((s) => s.value!));
  return (
    <section className={panel.panel} aria-label={data.title}>
      <h2 className={panel.title}>{data.title}</h2>
      <div className={panel.period}>{period}</div>
      {measured.length === 0 ? (
        // Nothing measured is not the same as measured zero, and must not read like it.
        <p className={panel.noData}>No data for this period.</p>
      ) : (
        <table className={panel.rows}>
          <tbody>
            {measured.map((s) => (
              <tr key={s.label}>
                <td className={panel.label}>{s.label}</td>
                <td className={panel.bar}>
                  <span style={{ width: `${(s.value! / max) * 100}%` }} />
                </td>
                <td className={panel.value}>
                  {format(s.value!, data.unit)} <span className={panel.unit}>{data.unit}</span>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}

/** Things counted are whole; a rate's extrapolation is not a tenth of a turn anyone had. */
const COUNTED = new Set(['turns', 'calls', 'tokens']);

function format(value: number, unit: string): string {
  if (COUNTED.has(unit)) return Math.round(value).toLocaleString();
  if (value >= 100) return Math.round(value).toLocaleString();
  if (value >= 1) return value.toFixed(1);
  return value.toFixed(3);
}
