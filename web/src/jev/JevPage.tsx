import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import type {
  AnswerCheckStats,
  GuardrailStats,
  JevOverview,
  JevStatsReport,
  RelevanceStats,
  RoutingStats,
  DomainStats,
} from '../api/types';
import { useApi, useAuth } from '../auth/useAuth';
import page from '../components/Page.module.css';
import { StopHint } from '../components/StopHint';
import { useEscToStop } from '../components/useEscToStop';
import { Bars, Columns, Legend, Lines } from '../intents/charts';
import { EvalHistory, IntentSection, Kpi, Panel } from '../intents/IntentSection';
import styles from '../intents/IntentStats.module.css';
import { bucketLabel, formatMs, percent } from '../intents/scale';

/** The windows the server will answer for; trace retention is seven days, so nothing longer exists. */
const WINDOWS: { id: string; label: string }[] = [
  { id: '1h', label: 'Last hour' },
  { id: '24h', label: 'Last 24 hours' },
  { id: '7d', label: 'Last 7 days' },
];

// The three categorical slots the intent screen validated, plus a neutral fourth for "no real answer".
const PASS = { label: 'pass', className: styles.seriesUsed };
const BLOCKED = { label: 'blocked', className: styles.seriesGated };
const WITHHELD = { label: 'withheld', className: styles.seriesFailed };
const UNSCREENED = { label: 'unscreened', className: styles.seriesMuted };

/**
 * Everything Jev did for this firm's chat turns, in one place (the A2A path has no turn trace and is not counted): a cross-cutting requests/availability overview on top, then a
 * section per call site — intent, guardrail, relevance & rerank, tool routing, the answer check. Aggregates only; no question, answer or
 * passage is on this screen.
 */
export function JevPage() {
  const { session } = useAuth();
  const api = useApi();
  const [window, setWindow] = useState('24h');

  const stats = useQuery({
    queryKey: ['admin', 'jev-stats', window, session?.token],
    queryFn: ({ signal }) =>
      api<JevStatsReport>(`/api/admin/jev-stats?window=${window}`, { signal }),
    enabled: !!session,
    refetchInterval: 60_000,
  });
  // A read still loading stops on Esc (stop-anything); the screen keeps what it last showed.
  const queryClient = useQueryClient();
  useEscToStop(
    stats.isFetching,
    () => void queryClient.cancelQueries({ queryKey: ['admin', 'jev-stats'] }),
  );

  const period = WINDOWS.find((w) => w.id === window)?.label ?? window;
  const r = stats.data;
  const s = r?.overview.settings;

  return (
    <div className={`${page.page} ${styles.root}`}>
      <h1 className={page.heading}>Jev</h1>
      <p className={page.muted}>
        Everything TypeSafe&apos;s Jev did for your firm&apos;s chat turns — classifying intent,
        screening what the assistant reads, judging and ordering searches, routing data turns and
        checking the final answer — and how often it was unavailable across all of them. Numbers
        only: no question, answer or passage is shown here.
      </p>
      <p className={page.muted}>
        Jev calls made by chat turns — the A2A partner path is not counted: its screenings are
        logged, not traced.
      </p>

      <div className={styles.controls}>
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
        {s && (
          <span className={page.muted}>
            {s.model} · guard {s.guardEnabled ? 'on' : 'off'} (block {s.promptBlockAt}, withhold{' '}
            {s.contentWithholdAt}) · relevance floor {s.relevanceFloor ?? '–'}
          </span>
        )}
      </div>

      {stats.isLoading && <p className={page.muted}>Loading the numbers…</p>}
      {stats.isFetching && <StopHint stopping={false} />}
      {stats.isError && (
        <p className={page.error} role="alert">
          Could not load the Jev statistics.
        </p>
      )}

      {r && r.overview.requests === 0 && r.intent.totals.classified === 0 && (
        <p className={styles.noData}>
          Jev made no request in this period ({period.toLowerCase()}).
        </p>
      )}

      {r && (r.overview.requests > 0 || r.intent.totals.classified > 0) && (
        <>
          <OverviewSection o={r.overview} period={period} bucketMinutes={r.bucketMinutes} />

          <h2 className={page.heading}>Intent</h2>
          {r.intent.totals.classified > 0 ? (
            <IntentSection r={r.intent} period={period} />
          ) : (
            <p className={styles.noData}>No turn was classified by Jev in this period.</p>
          )}

          <h2 className={page.heading}>Guardrail</h2>
          <GuardrailSection g={r.guardrail} period={period} bucketMinutes={r.bucketMinutes} />

          <h2 className={page.heading}>Relevance &amp; rerank</h2>
          <RelevanceSection rel={r.relevance} period={period} />

          <h2 className={page.heading}>Tool routing</h2>
          <RoutingSection routing={r.routing} period={period} />

          <h2 className={page.heading}>Domains</h2>
          <DomainsSection d={r.domains ?? null} period={period} />

          <h2 className={page.heading}>Answer check</h2>
          <AnswerCheckSection a={r.answerCheck ?? null} period={period} />
        </>
      )}

      <h2 className={page.heading}>Intent eval history</h2>
      <EvalHistory />
    </div>
  );
}

function timeAxis(starts: string[], bucketMinutes: number) {
  const everyNth = Math.max(1, Math.ceil(starts.length / 6));
  return {
    labels: starts.map((iso) => bucketLabel(iso, bucketMinutes)),
    ticks: starts
      .map((iso, i) => ({ at: i, label: bucketLabel(iso, bucketMinutes) }))
      .filter((_, i) => i % everyNth === 0),
  };
}

function OverviewSection({
  o,
  period,
  bucketMinutes,
}: {
  o: JevOverview;
  period: string;
  bucketMinutes: number;
}) {
  const axis = timeAxis(
    o.timeline.map((b) => b.start),
    bucketMinutes,
  );
  // Calls an open circuit skipped sent nothing; drawn as their own series so an outage stays visible once the
  // breaker has opened (add-jev-circuit-breaker).
  const skipped = o.skipped ?? 0;
  const maxReq = Math.max(1, ...o.timeline.map((b) => Math.max(b.requests, b.skipped ?? 0)));
  const anyRequests = o.timeline.some((b) => b.requests > 0 || (b.skipped ?? 0) > 0);
  const series = [
    { label: 'requests', className: styles.seriesUsed, values: o.timeline.map((b) => b.requests) },
    {
      label: 'unavailable',
      className: styles.seriesGated,
      values: o.timeline.map((b) => b.unavailable),
    },
    ...(skipped > 0
      ? [
          {
            label: 'skipped (circuit open)',
            className: styles.seriesFailed,
            values: o.timeline.map((b) => b.skipped ?? 0),
          },
        ]
      : []),
  ];

  return (
    <>
      <div className={styles.kpis}>
        <Kpi label="Jev requests" value={String(o.requests)} note={period} />
        <Kpi
          label="Unavailable"
          value={percent(o.unavailable, o.requests)}
          note={`${o.unavailable} of ${o.requests}`}
        />
        <Kpi label="Skipped" value={String(skipped)} note="circuit open · nothing sent" />
        <Kpi
          label="Requests per turn"
          value={o.requestsPerTurn == null ? '–' : o.requestsPerTurn.toFixed(2)}
          note={`${o.turns} classified turns`}
        />
        <Kpi
          label="Sites in use"
          value={String(o.sites.filter((x) => x.requests > 0).length)}
          note="intent · guardrail · relevance · answer"
        />
      </div>

      <div className={styles.grid2}>
        <Panel
          title="Jev requests over time"
          note={`${period} · total requests, the unavailable ones and the calls an open circuit skipped, ${bucketMinutes}-minute buckets`}
        >
          <Legend series={series.map(({ label, className }) => ({ label, className }))} />
          {anyRequests ? (
            <Lines
              ariaLabel="Jev requests over time"
              lines={series}
              xLabels={axis.labels}
              xTicks={axis.ticks}
              yDomain={[0, maxReq * 1.05]}
              format={(v) => String(Math.round(v))}
            />
          ) : (
            <p className={styles.noData}>No Jev request in this period.</p>
          )}
        </Panel>

        <Panel title="By call site" note={`${period} · each site that makes its own Jev request`}>
          <table className={styles.tableSmall} aria-label="Requests by call site">
            <thead>
              <tr>
                <th>Site</th>
                <th className={styles.num}>requests</th>
                <th className={styles.num}>unavailable</th>
                <th className={styles.num}>skipped</th>
                <th className={styles.num}>p50</th>
                <th className={styles.num}>p90</th>
              </tr>
            </thead>
            <tbody>
              {o.sites.map((site) => (
                <tr key={site.site}>
                  <td>{site.site}</td>
                  <td className={styles.num}>{site.requests}</td>
                  <td className={styles.num}>
                    {site.unavailable} ({percent(site.unavailable, site.requests)})
                  </td>
                  <td className={styles.num}>{site.skipped ?? 0}</td>
                  <td className={styles.num}>{formatMs(site.p50Ms)}</td>
                  <td className={styles.num}>{formatMs(site.p90Ms)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </Panel>
      </div>
    </>
  );
}

function GuardrailSection({
  g,
  period,
  bucketMinutes,
}: {
  g: GuardrailStats;
  period: string;
  bucketMinutes: number;
}) {
  if (g.screened === 0) {
    return (
      <p className={styles.noData}>Nothing was screened in this period ({period.toLowerCase()}).</p>
    );
  }
  const axis = timeAxis(
    g.timeline.map((b) => b.start),
    bucketMinutes,
  );
  const decisionSeries = [PASS, BLOCKED, WITHHELD, UNSCREENED];

  return (
    <>
      <div className={styles.kpis}>
        <Kpi label="Screenings" value={String(g.screened)} note={period} />
        <Kpi label="Blocked" value={String(g.blocked)} note="prompts refused" />
        <Kpi label="Withheld" value={String(g.withheld)} note="tool results / agents' words" />
        <Kpi label="Unscreened" value={String(g.unscreened)} note="Jev unavailable" />
      </div>

      <div className={styles.grid2}>
        <Panel title="Screenings by check" note={`${period} · what each screening decided`}>
          <Legend series={decisionSeries} />
          <Columns
            ariaLabel="Screenings by check"
            series={decisionSeries}
            unit="screenings"
            columns={g.checks.map((c) => ({
              label: c.check,
              values: [c.pass, c.blocked, c.withheld, c.unscreened],
            }))}
            xTicks={g.checks.map((c, i) => ({ at: i + 0.5, label: c.check }))}
          />
        </Panel>

        <Panel
          title="What tripped a block or a withholding"
          note={`${period} · the deciding guard question`}
        >
          {g.trippedBy.length === 0 ? (
            <p className={styles.noData}>No screening blocked or withheld in this period.</p>
          ) : (
            <Bars
              ariaLabel="Guard questions that tripped"
              rows={g.trippedBy.map((q) => ({
                label: q.question,
                value: q.count,
                className: q.decision === 'blocked' ? BLOCKED.className : WITHHELD.className,
                tip: [q.question, `${q.decision}: ${q.count}`],
              }))}
            />
          )}
        </Panel>

        <Panel
          title="Screenings over time"
          note={`${period} · by decision, the unscreened (Jev-unavailable) ones marked`}
        >
          <Legend series={decisionSeries} />
          <Columns
            ariaLabel="Screenings over time"
            series={decisionSeries}
            unit="screenings"
            columns={g.timeline.map((b, i) => ({
              label: axis.labels[i],
              values: [
                b.screened - b.blocked - b.withheld - b.unscreened,
                b.blocked,
                b.withheld,
                b.unscreened,
              ],
            }))}
            xTicks={axis.ticks}
          />
        </Panel>

        <Panel
          title="Screening latency"
          note={`${period} · ${g.latency.count} content screenings (each its own Jev request), 100 ms bins`}
        >
          <LatencyHistogram latency={g.latency} budgetLabel="timeout 2s" />
        </Panel>
      </div>
    </>
  );
}

function RelevanceSection({ rel, period }: { rel: RelevanceStats; period: string }) {
  if (rel.searches === 0) {
    return (
      <p className={styles.noData}>
        No search was judged by Jev in this period ({period.toLowerCase()}).
      </p>
    );
  }
  const anyMax = rel.maxHistogram.some((b) => b.kept + b.gated > 0);
  const kept = { label: 'kept', className: styles.seriesUsed };
  const gated = { label: 'silenced', className: styles.seriesGated };

  return (
    <>
      <div className={styles.kpis}>
        <Kpi label="Searches judged" value={String(rel.searches)} note={period} />
        <Kpi
          label="Silenced by the gate"
          value={percent(rel.gated, rel.searches)}
          note={`${rel.gated} searches`}
        />
        <Kpi
          label="Reranked by Jev"
          value={percent(rel.reranked, rel.searches)}
          note={`${rel.reranked} searches`}
        />
        <Kpi
          label="Unavailable"
          value={percent(rel.unavailable, rel.searches)}
          note={`${rel.unavailable} left ungated`}
        />
      </div>

      {rel.byDomain && rel.byDomain.length > 1 && (
        <div className={styles.kpis} aria-label="Judged searches per domain">
          {rel.byDomain.map((d) => (
            <Kpi
              key={d.domain}
              label={`${d.domain} documentation`}
              value={String(d.searches)}
              note={`${d.gated} silenced · ${d.unavailable} ungated`}
            />
          ))}
        </div>
      )}

      <div className={styles.grid2}>
        <Panel
          title="Top relevance per search"
          note={`${period} · the maximum passage relevance, floor marked`}
        >
          <Legend series={[kept, gated]} />
          {anyMax ? (
            <Columns
              ariaLabel="Top relevance histogram"
              series={[kept, gated]}
              unit="searches"
              columns={rel.maxHistogram.map((b) => ({
                label: `${b.from.toFixed(2)}–${b.to.toFixed(2)}`,
                values: [b.kept, b.gated],
              }))}
              xTicks={[0, 0.25, 0.5, 0.75, 1].map((v) => ({
                at: v * rel.maxHistogram.length,
                label: String(v),
              }))}
              thresholds={
                rel.floor == null
                  ? []
                  : [{ at: rel.floor * rel.maxHistogram.length, label: `floor ${rel.floor}` }]
              }
            />
          ) : (
            <p className={styles.noData}>No search carried a relevance score in the period.</p>
          )}
        </Panel>

        <Panel
          title="Judge latency"
          note={`${period} · ${rel.latency.count} searches, one Jev request each, 100 ms bins`}
        >
          <LatencyHistogram latency={rel.latency} budgetLabel="timeout 2s" />
        </Panel>
      </div>
    </>
  );
}

/** Where Jev placed the turns among the domains, how often a turn crossed, and whether the calls went where Jev said. */
function DomainsSection({ d, period }: { d: DomainStats | null; period: string }) {
  if (!d || d.judged === 0) {
    return (
      <p className={styles.noData}>
        No turn carried a domain verdict in this period ({period.toLowerCase()}).
      </p>
    );
  }
  return (
    <div className={styles.kpis}>
      <Kpi label="Turns with a verdict" value={String(d.judged)} note={period} />
      <Kpi label="Billing only" value={percent(d.billing, d.judged)} note={`${d.billing} turns`} />
      <Kpi
        label="Portfolio only"
        value={percent(d.portfolio, d.judged)}
        note={`${d.portfolio} turns`}
      />
      <Kpi label="Both (crossing)" value={percent(d.both, d.judged)} note={`${d.both} turns`} />
      <Kpi label="Neither" value={percent(d.none, d.judged)} note={`${d.none} turns`} />
      <Kpi
        label="Crossed by the calls"
        value={percent(d.crossed, d.judged)}
        note={`${d.crossed} turns went from one server's tools to the other's`}
      />
      <Kpi
        label="Calls matched the verdict"
        value={percent(d.agreed, d.withCalls)}
        note={`${d.agreed} of ${d.withCalls} turns that called a tool`}
      />
    </div>
  );
}

/**
 * Jev's check of the final answer: how many answers got a verdict, the share below each floor, how many fell in the
 * review band (uncertain, no signal), how many were left unchecked, and the check's latency. The check flags a turn for
 * review; it never blocks the answer.
 */
function AnswerCheckSection({ a, period }: { a: AnswerCheckStats | null; period: string }) {
  if (!a || a.answers === 0) {
    return (
      <p className={styles.noData}>
        No answer was checked by Jev in this period ({period.toLowerCase()}).
      </p>
    );
  }
  return (
    <>
      <div className={styles.kpis}>
        <Kpi
          label="Answers checked"
          value={String(a.checked)}
          note={`${a.answers} answered turns`}
        />
        <Kpi
          label="Not relevant"
          value={percent(a.notRelevant, a.checked)}
          note={`${a.notRelevant} below floor ${a.relevantFloor ?? '–'}`}
        />
        <Kpi
          label="Not grounded"
          value={percent(a.notGrounded, a.checked)}
          note={`${a.notGrounded} below floor ${a.groundedFloor ?? '–'}`}
        />
        {a.uncertain !== undefined && (
          <Kpi
            label="Uncertain"
            value={percent(a.uncertain, a.checked)}
            note={`${a.uncertain} in the review band · no signal`}
          />
        )}
        <Kpi
          label="Unchecked"
          value={String(a.unchecked)}
          note={`${a.unavailable} Jev unavailable · the rest off, no key or over the cap`}
        />
      </div>
      <div className={styles.grid2}>
        <Panel
          title="Answer check latency"
          note={`${period} · ${a.latency.count} checks, one Jev request each, added to the turn, 100 ms bins`}
        >
          <LatencyHistogram latency={a.latency} budgetLabel="timeout 3s" />
        </Panel>
      </div>
    </>
  );
}

function RoutingSection({ routing, period }: { routing: RoutingStats; period: string }) {
  if (routing.dataTurns === 0) {
    return (
      <p className={styles.noData}>
        No data turn in this period ({period.toLowerCase()})
        {routing.enabled ? '' : ' — routing is off'}.
      </p>
    );
  }
  return (
    <>
      <div className={styles.kpis}>
        <Kpi label="Data turns" value={String(routing.dataTurns)} note={period} />
        <Kpi
          label="Routed by Jev"
          value={percent(routing.routed, routing.dataTurns)}
          note={`${routing.routed} turns`}
        />
        <Kpi
          label="Model calls · routed"
          value={
            routing.modelCallsRoutedMedian == null ? '–' : routing.modelCallsRoutedMedian.toFixed(2)
          }
          note="median per data turn"
        />
        <Kpi
          label="Model calls · unrouted"
          value={
            routing.modelCallsUnroutedMedian == null
              ? '–'
              : routing.modelCallsUnroutedMedian.toFixed(2)
          }
          note="median per data turn"
        />
      </div>

      <div className={styles.grid2}>
        <Panel title="Routed to which tool" note={`${period} · the read tool Jev pre-routed`}>
          {routing.tools.length === 0 ? (
            <p className={styles.noData}>No data turn was routed in this period.</p>
          ) : (
            <Bars
              ariaLabel="Routed tools"
              className={styles.seriesSingle}
              rows={routing.tools.map((t) => ({ label: t.tool, value: t.count }))}
            />
          )}
        </Panel>

        <Panel title="Why a data turn was not routed" note={`${period} · the router's reason`}>
          {routing.notRoutedReasons.length === 0 ? (
            <p className={styles.noData}>Every data turn was routed in this period.</p>
          ) : (
            <Bars
              ariaLabel="Reasons a data turn was not routed"
              className={styles.seriesGated}
              rows={routing.notRoutedReasons.map((x) => ({ label: x.reason, value: x.count }))}
            />
          )}
        </Panel>
      </div>
    </>
  );
}

/** A latency histogram + percentile table, from an IntentLatency (reused by the guardrail and relevance sections). */
function LatencyHistogram({
  latency,
  budgetLabel,
}: {
  latency: RelevanceStats['latency'];
  budgetLabel: string;
}) {
  if (latency.count === 0) {
    return <p className={styles.noData}>No timed request in this period.</p>;
  }
  return (
    <>
      <Columns
        ariaLabel="Latency histogram"
        series={[{ label: 'requests', className: styles.seriesSingle }]}
        unit="requests"
        columns={latency.bins.map((b) => ({
          label:
            b.toMs == null
              ? `≥ ${formatMs(b.fromMs)}`
              : `${formatMs(b.fromMs)}–${formatMs(b.toMs)}`,
          values: [b.count],
        }))}
        xTicks={latency.bins
          .map((b, i) => ({ at: i, label: formatMs(b.fromMs) }))
          .filter((_, i) => i % 5 === 0)}
        thresholds={[{ at: latency.bins.length - 1, label: budgetLabel }]}
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
            <td>{formatMs(latency.p50)}</td>
            <td>{formatMs(latency.p90)}</td>
            <td>{formatMs(latency.p99)}</td>
            <td>{formatMs(latency.max)}</td>
          </tr>
        </tbody>
      </table>
    </>
  );
}
