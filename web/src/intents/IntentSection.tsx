import { useQuery } from '@tanstack/react-query';
import { type ReactNode } from 'react';
import type {
  EvalReportSummary,
  IntentChoiceCount,
  IntentOutcome,
  IntentProbabilityBin,
  IntentStatsReport,
} from '../api/types';
import { useApi, useAuth } from '../auth/useAuth';
import page from '../components/Page.module.css';
import { formatDate } from '../evals/format';
import { Bars, Columns, Legend, Lines, Scatter, type Series } from './charts';
import styles from './IntentStats.module.css';
import { PipelineDiagram } from './PipelineDiagram';
import { bucketLabel, formatMs, percent } from './scale';

const OUTCOMES: Record<IntentOutcome, Series> = {
  used: { label: 'used', className: styles.seriesUsed },
  gated: { label: 'gated', className: styles.seriesGated },
  failed: { label: 'failed', className: styles.seriesFailed },
};

const INTENTS = ['procedural', 'mixed', 'data', 'chitchat', 'other'];

/**
 * The intent section: what Jev answered on this firm's turns, what the floors did with it, and how fast it was.
 * A pure view of one {@link IntentStatsReport}; the Jev overview page feeds it `report.intent`.
 */
export function IntentSection({ r, period }: { r: IntentStatsReport; period: string }) {
  const t = r.totals;
  const timeoutMs = r.settings.timeoutSeconds * 1000;
  const timedOut = r.timeline.reduce((sum, b) => sum + b.timedOut, 0);
  const everyNth = Math.max(1, Math.ceil(r.timeline.length / 6));
  const timeTicks = r.timeline
    .map((b, i) => ({ at: i + 0.5, label: bucketLabel(b.start, r.bucketMinutes) }))
    .filter((_, i) => i % everyNth === 0);
  const timeLabels = r.timeline.map((b) => bucketLabel(b.start, r.bucketMinutes));

  return (
    <>
      <div className={styles.kpis}>
        <Kpi label="Classified turns" value={String(t.classified)} note={period} />
        <Kpi label="Used" value={percent(t.used, t.classified)} note={`${t.used} turns`} />
        <Kpi
          label="Gated by a floor"
          value={percent(t.gated, t.classified)}
          note={`${t.gated} turns`}
        />
        <Kpi
          label="Failed"
          value={percent(t.failed, t.classified)}
          note={`${t.failed} turns, ${timedOut} timed out`}
        />
        <Kpi
          label="Retrieval forced"
          value={percent(t.forced, t.classified)}
          note={`${t.forced} turns`}
        />
        <Kpi
          label="Latency p90"
          value={formatMs(r.latency.p90)}
          note={`budget ${formatMs(timeoutMs)}`}
        />
      </div>
      {t.excludedEvents > 0 && (
        <p className={styles.panelNote}>
          {t.excludedEvents} turn(s) in this period were classified by an earlier classifier and are
          left out of every number below.
        </p>
      )}

      <section
        className={`${styles.panel} ${styles.panelWide}`}
        aria-label="Classification pipeline"
      >
        <h2 className={styles.panelTitle}>How a question becomes a forced search</h2>
        <p className={styles.panelNote}>
          {period} · turns along each edge. One Jev call asks for the intent (a Choice) and for the
          probability the question is about billing (a Noul); code applies the floors.
        </p>
        <PipelineDiagram counts={r.pipeline} settings={r.settings} />
      </section>

      <div className={styles.grid2}>
        <Panel
          title="Turns over time"
          note={`${period} · by outcome, ${r.bucketMinutes}-minute buckets`}
        >
          <Legend series={[OUTCOMES.used, OUTCOMES.gated, OUTCOMES.failed]} />
          <Columns
            ariaLabel="Turns over time by outcome"
            series={[OUTCOMES.used, OUTCOMES.gated, OUTCOMES.failed]}
            columns={r.timeline.map((b, i) => ({
              label: timeLabels[i],
              values: [b.used, b.gated, b.failed],
            }))}
            xTicks={timeTicks}
          />
          <details className={styles.details}>
            <summary>Table</summary>
            <table className={styles.tableSmall}>
              <thead>
                <tr>
                  <th>Bucket</th>
                  <th className={styles.num}>used</th>
                  <th className={styles.num}>gated</th>
                  <th className={styles.num}>failed</th>
                  <th className={styles.num}>timed out</th>
                  <th className={styles.num}>p50</th>
                  <th className={styles.num}>p90</th>
                </tr>
              </thead>
              <tbody>
                {r.timeline
                  .map((b, i) => ({ b, label: timeLabels[i] }))
                  .filter(({ b }) => b.used + b.gated + b.failed > 0)
                  .map(({ b, label }) => (
                    <tr key={b.start}>
                      <td>{label}</td>
                      <td className={styles.num}>{b.used}</td>
                      <td className={styles.num}>{b.gated}</td>
                      <td className={styles.num}>{b.failed}</td>
                      <td className={styles.num}>{b.timedOut}</td>
                      <td className={styles.num}>{formatMs(b.p50Ms)}</td>
                      <td className={styles.num}>{formatMs(b.p90Ms)}</td>
                    </tr>
                  ))}
              </tbody>
            </table>
          </details>
        </Panel>

        <Panel
          title="Latency over time"
          note={`${period} · median and p90 per bucket, timeout marked`}
        >
          <Legend
            series={[
              { label: 'p50', className: styles.seriesUsed },
              { label: 'p90', className: styles.seriesGated },
            ]}
          />
          <Lines
            ariaLabel="Latency over time"
            lines={[
              {
                label: 'p50',
                className: styles.seriesUsed,
                values: r.timeline.map((b) => b.p50Ms),
              },
              {
                label: 'p90',
                className: styles.seriesGated,
                values: r.timeline.map((b) => b.p90Ms),
              },
            ]}
            xLabels={timeLabels}
            xTicks={timeTicks.map((x) => ({ ...x, at: x.at - 0.5 }))}
            yDomain={[0, Math.max(timeoutMs, ...r.timeline.map((b) => b.p90Ms ?? 0)) * 1.05]}
            format={formatMs}
            thresholds={[{ value: timeoutMs, label: `timeout ${r.settings.timeoutSeconds}s` }]}
          />
        </Panel>

        <Panel
          title="Timeouts over time"
          note={`${period} · calls that did not answer within ${r.settings.timeoutSeconds}s`}
        >
          {timedOut === 0 ? (
            <p className={styles.noData}>No call timed out in this period.</p>
          ) : (
            <Columns
              ariaLabel="Timeouts over time"
              series={[{ label: 'timed out', className: styles.seriesFailed }]}
              columns={r.timeline.map((b, i) => ({ label: timeLabels[i], values: [b.timedOut] }))}
              xTicks={timeTicks}
            />
          )}
        </Panel>

        <Panel
          title="Outcomes by reason"
          note={`${period} · every classification, and why an answer was not used`}
        >
          <Legend series={[OUTCOMES.used, OUTCOMES.gated, OUTCOMES.failed]} />
          <Bars
            ariaLabel="Outcomes by reason"
            rows={r.reasons.map((x) => ({
              label: x.label,
              value: x.count,
              className: OUTCOMES[x.outcome].className,
              tip: [x.label, `${x.outcome}: ${x.count} (${percent(x.count, t.classified)})`],
            }))}
          />
        </Panel>

        <Panel
          title="Jev's choice vs the intent the turn used"
          note={`${period} · off the diagonal, a floor overruled Jev (the turn went on as other)`}
        >
          <ChoiceMatrix choices={r.choices} />
        </Panel>

        <Panel title="Mean probability per intent" note={`${period} · over every answer Jev gave`}>
          <Bars
            ariaLabel="Mean probability per intent"
            max={1}
            format={(v) => v.toFixed(2)}
            className={styles.seriesSingle}
            rows={r.meanProbabilities.map((m) => ({
              label: m.intent,
              value: m.mean,
              tip: [m.intent, `mean probability ${m.mean.toFixed(3)}`, `chosen ${m.chosen} times`],
            }))}
          />
        </Panel>

        <Panel title="Confidence" note={`${period} · Jev's confidence in its choice, floor marked`}>
          <Legend series={[OUTCOMES.used, OUTCOMES.gated]} />
          <ProbabilityHistogram
            bins={r.confidence}
            floor={r.settings.minConfidence}
            floorLabel={`floor ${r.settings.minConfidence}`}
            ariaLabel="Confidence histogram"
          />
        </Panel>

        <Panel
          title="In the billing domain"
          note={`${period} · Jev's probability the question is about billing, floor marked`}
        >
          <Legend series={[OUTCOMES.used, OUTCOMES.gated]} />
          <ProbabilityHistogram
            bins={r.inDomain}
            floor={r.settings.minInDomain}
            floorLabel={`floor ${r.settings.minInDomain}`}
            ariaLabel="In-domain histogram"
          />
        </Panel>

        <Panel
          title="Confidence × in-domain"
          note={`${period} · each answered turn; only procedural and mixed answers are held to the in-domain floor`}
        >
          <Legend series={[OUTCOMES.used, OUTCOMES.gated]} />
          <Scatter
            ariaLabel="Confidence against in-domain probability"
            xLabel="confidence"
            yLabel="in-domain"
            xFloor={r.settings.minConfidence}
            yFloor={r.settings.minInDomain}
            points={r.points.map((p) => ({
              x: p.confidence,
              y: p.inDomain,
              className: OUTCOMES[p.outcome].className,
              tip: [
                p.choice,
                `confidence ${p.confidence.toFixed(2)}`,
                `in-domain ${p.inDomain.toFixed(2)}`,
                p.outcome,
              ],
            }))}
          />
        </Panel>

        <Panel
          title="Latency"
          note={`${period} · ${r.latency.count} timed calls, 100 ms bins, timeout marked`}
        >
          <Columns
            ariaLabel="Latency histogram"
            series={[{ label: 'calls', className: styles.seriesSingle }]}
            unit="calls"
            columns={r.latency.bins.map((b) => ({
              label:
                b.toMs == null
                  ? `≥ ${formatMs(b.fromMs)}`
                  : `${formatMs(b.fromMs)}–${formatMs(b.toMs)}`,
              values: [b.count],
            }))}
            xTicks={r.latency.bins
              .map((b, i) => ({ at: i, label: formatMs(b.fromMs) }))
              .filter((_, i) => i % 5 === 0)}
            thresholds={[
              { at: r.latency.bins.length - 1, label: `timeout ${r.settings.timeoutSeconds}s` },
            ]}
          />
          <table className={styles.tableSmall}>
            <thead>
              <tr>
                <th>p50</th>
                <th>p90</th>
                <th>p99</th>
                <th>max</th>
              </tr>
            </thead>
            <tbody>
              <tr>
                <td>{formatMs(r.latency.p50)}</td>
                <td>{formatMs(r.latency.p90)}</td>
                <td>{formatMs(r.latency.p99)}</td>
                <td>{formatMs(r.latency.max)}</td>
              </tr>
            </tbody>
          </table>
        </Panel>

        <Panel title="Model versions" note={`${period} · the version Jev reported, per turn`}>
          <Bars
            ariaLabel="Model versions"
            className={styles.seriesSingle}
            rows={r.models.map((m) => ({ label: m.model, value: m.count }))}
          />
        </Panel>
      </div>
    </>
  );
}

export function Kpi({ label, value, note }: { label: string; value: string; note: string }) {
  return (
    <div className={styles.kpi}>
      <div className={styles.kpiLabel}>{label}</div>
      <div className={styles.kpiValue}>{value}</div>
      <div className={styles.kpiNote}>{note}</div>
    </div>
  );
}

export function Panel({
  title,
  note,
  children,
}: {
  title: string;
  note: string;
  children: ReactNode;
}) {
  return (
    <section className={styles.panel} aria-label={title}>
      <h2 className={styles.panelTitle}>{title}</h2>
      <p className={styles.panelNote}>{note}</p>
      {children}
    </section>
  );
}

function ProbabilityHistogram({
  bins,
  floor,
  floorLabel,
  ariaLabel,
}: {
  bins: IntentProbabilityBin[];
  floor: number;
  floorLabel: string;
  ariaLabel: string;
}) {
  if (bins.every((b) => b.used + b.gated === 0)) {
    return <p className={styles.noData}>No answer carried this number in the period.</p>;
  }
  return (
    <Columns
      ariaLabel={ariaLabel}
      series={[OUTCOMES.used, OUTCOMES.gated]}
      columns={bins.map((b) => ({
        label: `${b.from.toFixed(2)}–${b.to.toFixed(2)}`,
        values: [b.used, b.gated],
      }))}
      xTicks={[0, 0.25, 0.5, 0.75, 1].map((v) => ({ at: v * bins.length, label: String(v) }))}
      thresholds={[{ at: floor * bins.length, label: floorLabel }]}
    />
  );
}

/** Rows are Jev's choice, columns the intent the turn went on with; shading is the share of all turns. */
function ChoiceMatrix({ choices }: { choices: IntentChoiceCount[] }) {
  const rows = [...INTENTS, 'none'].filter((c) => choices.some((x) => x.choice === c));
  const cols = INTENTS.filter((i) => choices.some((x) => x.intent === i));
  const max = Math.max(1, ...choices.map((c) => c.count));
  const count = (choice: string, intent: string) =>
    choices.find((c) => c.choice === choice && c.intent === intent)?.count ?? 0;
  return (
    <table className={styles.matrix} aria-label="Choice against intent used">
      <thead>
        <tr>
          <th>Jev chose ↓ / turn used →</th>
          {cols.map((c) => (
            <th key={c} scope="col">
              {c}
            </th>
          ))}
        </tr>
      </thead>
      <tbody>
        {rows.map((row) => (
          <tr key={row}>
            <th scope="row">{row === 'none' ? 'no answer' : row}</th>
            {cols.map((col) => {
              const n = count(row, col);
              const share = n / max;
              return (
                <td
                  key={col}
                  className={`${styles.cell} ${share > 0.55 ? styles.cellStrong : ''}`}
                  style={{
                    background:
                      n === 0
                        ? undefined
                        : `color-mix(in srgb, var(--used) ${Math.round(15 + share * 85)}%, transparent)`,
                  }}
                  title={`${row} → ${col}: ${n}`}
                >
                  {n || ''}
                </td>
              );
            })}
          </tr>
        ))}
      </tbody>
    </table>
  );
}

/** The offline intent eval over the runs this machine has kept, read from the eval report endpoint. */
export function EvalHistory() {
  const { session } = useAuth();
  const api = useApi();
  const reports = useQuery({
    queryKey: ['evals', 'reports', session?.token],
    queryFn: ({ signal }) => api<EvalReportSummary[]>('/api/evals/reports', { signal }),
    enabled: !!session,
  });

  const runs = (reports.data ?? [])
    .filter((x) => x.suite === 'intent' && x.variants.length > 0)
    .sort((a, b) => a.startedAt.localeCompare(b.startedAt));
  const latest = runs[runs.length - 1];
  const metric = (key: string) => runs.map((x) => x.variants[0].metrics[key] ?? null);
  const labels = runs.map((x) => formatDate(x.startedAt));
  const low = Math.min(0.9, ...runs.flatMap((x) => Object.values(x.variants[0].metrics)));
  const yDomain: [number, number] = [Math.floor(low * 20) / 20, 1];
  const every = Math.max(1, Math.ceil(runs.length / 4));
  const xTicks = runs
    .map((_, i) => ({ at: i, label: `#${i + 1}` }))
    .filter((_, i) => i % every === 0);
  const threshold = latest?.variants[0].thresholds.accuracy;

  return (
    <section className={`${styles.panel} ${styles.panelWide}`} aria-label="Intent eval history">
      <h2 className={styles.panelTitle}>Intent eval history</h2>
      <p className={styles.panelNote}>
        <code>make eval-intent</code> runs on this machine: the classifier alone over labelled
        questions in English, Bulgarian and Bulgarian in Latin letters.
      </p>
      {reports.isError && (
        <p className={page.error} role="alert">
          Could not load the eval reports.
        </p>
      )}
      {reports.data && runs.length === 0 && (
        <p className={styles.noData}>No intent eval run on this machine yet.</p>
      )}
      {latest && (
        <>
          <div className={styles.grid2}>
            <div>
              <h3 className={styles.panelTitle}>Accuracy per language</h3>
              <Legend
                series={[
                  { label: 'en', className: styles.seriesUsed },
                  { label: 'bg', className: styles.seriesGated },
                  { label: 'bg-latn', className: styles.seriesFailed },
                ]}
              />
              <Lines
                ariaLabel="Intent eval accuracy per language"
                lines={[
                  { label: 'en', className: styles.seriesUsed, values: metric('accuracy:en') },
                  { label: 'bg', className: styles.seriesGated, values: metric('accuracy:bg') },
                  {
                    label: 'bg-latn',
                    className: styles.seriesFailed,
                    values: metric('accuracy:bg-latn'),
                  },
                ]}
                xLabels={labels}
                xTicks={xTicks}
                yDomain={yDomain}
                format={(v) => v.toFixed(2)}
                thresholds={
                  threshold == null ? [] : [{ value: threshold, label: `gate ${threshold}` }]
                }
              />
            </div>
            <div>
              <h3 className={styles.panelTitle}>Accuracy per split</h3>
              <Legend
                series={[
                  { label: 'design', className: styles.seriesUsed },
                  { label: 'holdout', className: styles.seriesGated },
                ]}
              />
              <Lines
                ariaLabel="Intent eval accuracy per split"
                lines={[
                  {
                    label: 'design',
                    className: styles.seriesUsed,
                    values: metric('accuracy:design'),
                  },
                  {
                    label: 'holdout',
                    className: styles.seriesGated,
                    values: metric('accuracy:holdout'),
                  },
                ]}
                xLabels={labels}
                xTicks={xTicks}
                yDomain={yDomain}
                format={(v) => v.toFixed(2)}
                thresholds={
                  threshold == null ? [] : [{ value: threshold, label: `gate ${threshold}` }]
                }
              />
            </div>
          </div>
          <h3 className={styles.panelTitle}>
            Latest run · {formatDate(latest.startedAt)} · {latest.passed ? 'passed' : 'failed'} ·{' '}
            {latest.variants[0].cases} cases
          </h3>
          <table className={styles.tableSmall} aria-label="Latest intent eval metrics">
            <thead>
              <tr>
                <th>Metric</th>
                <th className={styles.num}>Value</th>
                <th className={styles.num}>Gate</th>
              </tr>
            </thead>
            <tbody>
              {Object.entries(latest.variants[0].metrics).map(([name, value]) => (
                <tr key={name}>
                  <td>{name}</td>
                  <td className={styles.num}>{value.toFixed(3)}</td>
                  <td className={styles.num}>
                    {latest.variants[0].thresholds[name]?.toFixed(2) ?? ''}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {latest.variants[0].failures.length > 0 && (
            <>
              <h3 className={styles.panelTitle}>Failures in the latest run</h3>
              <table className={styles.tableSmall} aria-label="Latest intent eval failures">
                <thead>
                  <tr>
                    <th>Case</th>
                    <th>Why</th>
                  </tr>
                </thead>
                <tbody>
                  {latest.variants[0].failures.map((f) => (
                    <tr key={f.caseId}>
                      <td className={page.mono}>{f.caseId}</td>
                      <td>{f.reason}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </>
          )}
        </>
      )}
    </section>
  );
}
