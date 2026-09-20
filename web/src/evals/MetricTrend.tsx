import { useMemo, useState } from 'react';
import type { EvalReportSummary } from '../api/types';
import page from '../components/Page.module.css';
import styles from './MetricTrend.module.css';
import { formatDate } from './format';

type Point = { runId: string; at: string; value: number };

const WIDTH = 520;
const HEIGHT = 120;
const PAD = 8;

/**
 * A metric across past runs, with the accepted baseline marked. The history is whatever reports this machine has —
 * the gate itself never depends on them, which is why the baseline is a committed file.
 */
export function MetricTrend({ reports }: { reports: EvalReportSummary[] }) {
  const options = useMemo(() => series(reports), [reports]);
  const [key, setKey] = useState<string | null>(null);
  const chosen = options.find((o) => o.key === key) ?? options[0];

  if (options.length === 0) {
    return null;
  }

  return (
    <section className={styles.panel} aria-label="Metric trend">
      <div className={styles.header}>
        <h2 className={styles.heading}>Trend</h2>
        <select
          aria-label="Metric"
          value={chosen.key}
          onChange={(event) => setKey(event.target.value)}
          className={styles.picker}
        >
          {options.map((o) => (
            <option key={o.key} value={o.key}>
              {o.label}
            </option>
          ))}
        </select>
      </div>

      {chosen.points.length < 2 ? (
        <p className={page.muted}>
          Not enough history yet — {chosen.points.length} run of this suite on this machine. Run the
          suite again to see a trend.
        </p>
      ) : (
        <Chart points={chosen.points} baseline={chosen.baseline} label={chosen.label} />
      )}
    </section>
  );
}

function Chart({
  points,
  baseline,
  label,
}: {
  points: Point[];
  baseline: number | null;
  label: string;
}) {
  const values = [...points.map((p) => p.value), ...(baseline === null ? [] : [baseline])];
  const min = Math.min(...values);
  const max = Math.max(...values);
  // A flat series must not collapse to a zero-height band.
  const span = max - min < 0.05 ? 0.05 : max - min;
  const low = min - span * 0.1;
  const y = (value: number) => HEIGHT - PAD - ((value - low) / (span * 1.2)) * (HEIGHT - PAD * 2);
  const x = (index: number) =>
    PAD + (points.length === 1 ? 0 : (index / (points.length - 1)) * (WIDTH - PAD * 2));

  return (
    <>
      <svg
        viewBox={`0 0 ${WIDTH} ${HEIGHT}`}
        className={styles.chart}
        role="img"
        aria-label={`${label} over time`}
      >
        {baseline !== null && (
          <>
            <line
              x1={PAD}
              x2={WIDTH - PAD}
              y1={y(baseline)}
              y2={y(baseline)}
              className={styles.baseline}
            />
            <text
              x={WIDTH - PAD}
              y={y(baseline) - 4}
              textAnchor="end"
              className={styles.baselineLabel}
            >
              baseline {baseline.toFixed(3)}
            </text>
          </>
        )}
        <polyline
          points={points.map((p, i) => `${x(i)},${y(p.value)}`).join(' ')}
          className={styles.line}
          fill="none"
        />
        {points.map((p, i) => (
          <circle key={p.runId} cx={x(i)} cy={y(p.value)} r={3} className={styles.point}>
            <title>{`${formatDate(p.at)} — ${p.value.toFixed(3)}`}</title>
          </circle>
        ))}
      </svg>
      <p className={page.muted}>
        {points.length} runs · oldest {formatDate(points[0].at)} ({points[0].value.toFixed(3)}) ·
        newest {formatDate(points[points.length - 1].at)} (
        {points[points.length - 1].value.toFixed(3)})
      </p>
    </>
  );
}

/** One series per suite, variant and metric, oldest first; the baseline comes from the newest comparison. */
function series(reports: EvalReportSummary[]) {
  const byKey = new Map<string, { label: string; points: Point[]; baseline: number | null }>();
  const ordered = [...reports].sort((a, b) => a.startedAt.localeCompare(b.startedAt));

  for (const report of ordered) {
    for (const variant of report.variants) {
      for (const [metric, value] of Object.entries(variant.metrics)) {
        const key = `${report.suite}/${variant.name}/${metric}`;
        const entry = byKey.get(key) ?? { label: key, points: [], baseline: null };
        entry.points.push({ runId: report.runId, at: report.startedAt, value });
        const comparison = report.comparisons?.find(
          (c) => c.variant === variant.name && c.metric === metric,
        );
        // The newest run that knew a baseline wins, since reports are walked oldest first.
        if (comparison?.baseline != null) {
          entry.baseline = comparison.baseline;
        }
        byKey.set(key, entry);
      }
    }
  }
  return [...byKey.entries()]
    .map(([key, entry]) => ({ key, ...entry }))
    .sort((a, b) => b.points.length - a.points.length || a.key.localeCompare(b.key));
}
