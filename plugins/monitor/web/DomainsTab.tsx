import type { TraceEvent } from './types';
import panel from './MonitorPanel.module.css';
import styles from './DomainsTab.module.css';
import {
  domainColor,
  domainPath,
  type BoundaryData,
  type DomainData,
  type DomainToolCallData,
  type DomainToolResultData,
  type TurnEndDomainData,
} from './domainData';
import { byKind, dataOf, firstOf, formatMs } from './traceData';

function Badge({ domain }: { domain?: string }) {
  return (
    <span className={styles.badge} style={{ background: domainColor(domain) }}>
      {domain ?? 'unknown'}
    </span>
  );
}

/**
 * Where the question sits among the domains and where the turn crossed between them: Jev's verdict (each domain's
 * probability against the scope floor), then every tool call in order with the server that answered it, and the
 * boundary between two calls of different domains. The prediction and what the calls did are compared at the end.
 */
export function DomainsTab({ events }: { events: TraceEvent[] }) {
  const verdict = dataOf<DomainData>(firstOf(events, 'domain'));
  const hasVerdict = firstOf(events, 'domain') !== undefined;
  const calls = byKind(events, 'tool.call');
  const results = byKind(events, 'tool.result');
  const boundaries = byKind(events, 'boundary');
  const end = dataOf<TurnEndDomainData>(firstOf(events, 'turn.end'));
  const path = domainPath(events);

  if (!hasVerdict && calls.length === 0) {
    return <p className={panel.empty}>No domain verdict and no tool calls in this turn.</p>;
  }

  const probabilities = Object.entries(verdict.probabilities ?? {});
  const floor = verdict.scopeFloor ?? 0.5;
  const inScope = verdict.inScope ?? [];
  const predicted = end.domainsPredicted ?? inScope;
  const touched = end.domainsTouched ?? [...new Set(path)];
  const ended = firstOf(events, 'turn.end') !== undefined;
  const missed = predicted.filter((d) => !touched.includes(d));
  const unexpected = touched.filter((d) => !predicted.includes(d));

  return (
    <div>
      {hasVerdict && (
        <section className={panel.card} aria-label="Jev's domain verdict">
          <h3 className={panel.cardTitle}>Jev's domain verdict</h3>
          <div className={styles.verdict}>
            {probabilities.map(([domain, p]) => (
              <div key={domain} style={{ display: 'contents' }} data-testid={`domain-${domain}`}>
                <span className={styles.domainName}>{domain}</span>
                <span
                  className={styles.meter}
                  role="meter"
                  aria-label={`${domain} probability`}
                  aria-valuemin={0}
                  aria-valuemax={1}
                  aria-valuenow={p}
                >
                  <span
                    className={styles.fill}
                    style={{ width: `${Math.round(p * 100)}%`, background: domainColor(domain) }}
                  />
                  <span
                    className={styles.floor}
                    style={{ left: `${floor * 100}%` }}
                    title={`scope floor ${floor}`}
                  />
                </span>
                <span className={styles.value}>
                  {p.toFixed(2)} {inScope.includes(domain) ? '· in scope' : ''}
                </span>
              </div>
            ))}
          </div>
          <div className={styles.banner}>
            {verdict.crossing
              ? `Crosses the boundary: ${inScope.join(' ↔ ')}`
              : verdict.primary
                ? `One domain: ${verdict.primary}`
                : 'No domain in scope'}
          </div>
          <p className={styles.note}>
            Scope floor {floor.toFixed(2)}
            {verdict.forcedSearches && verdict.forcedSearches.length > 0
              ? ` · forced: ${verdict.forcedSearches.join(' + ')}`
              : ' · nothing forced'}
            {verdict.unavailable && verdict.unavailable.length > 0
              ? ` · not offered this turn: ${verdict.unavailable.join(', ')}`
              : ''}
          </p>
        </section>
      )}

      <section className={panel.card} aria-label="Path across servers">
        <h3 className={panel.cardTitle}>
          Path across servers
          {path.length > 0 && <span className={panel.chip}>{path.join(' → ')}</span>}
        </h3>
        {calls.length === 0 ? (
          <p className={panel.muted}>No tool calls.</p>
        ) : (
          <ol className={styles.path}>
            {calls.map((call) => {
              const c = dataOf<DomainToolCallData>(call);
              const r = dataOf<DomainToolResultData>(
                results.find((e) => dataOf<DomainToolResultData>(e).callId === c.callId),
              );
              const crossing = boundaries.find((b) => dataOf<BoundaryData>(b).callId === c.callId);
              const b = dataOf<BoundaryData>(crossing);
              return (
                <li key={call.seq}>
                  {crossing && (
                    <div className={styles.crossing} role="note" aria-label="Boundary crossing">
                      ⇣ crossed <Badge domain={b.from} /> → <Badge domain={b.to} />
                    </div>
                  )}
                  <div className={styles.hop} style={{ borderLeftColor: domainColor(c.domain) }}>
                    <Badge domain={c.domain} />
                    <span className={styles.tool}>{c.tool}</span>
                    {c.server && <span className={panel.chip}>{c.server}</span>}
                    {r.mcpInstance && <span className={panel.chip}>replica {r.mcpInstance}</span>}
                    {r.latencyMs !== undefined && (
                      <span className={panel.chip}>{formatMs(r.latencyMs)}</span>
                    )}
                    {r.isError ? (
                      <span className={`${panel.chip} ${panel.error}`}>error</span>
                    ) : r.callId ? (
                      <span className={`${panel.chip} ${panel.ok}`}>ok</span>
                    ) : (
                      <span className={panel.chip}>running…</span>
                    )}
                  </div>
                </li>
              );
            })}
          </ol>
        )}
      </section>

      {ended && hasVerdict && (
        <section className={panel.card} aria-label="Prediction and calls">
          <h3 className={panel.cardTitle}>Prediction and calls</h3>
          <div>
            Jev predicted: {predicted.length > 0 ? predicted.join(', ') : 'none'} · the calls
            touched: {touched.length > 0 ? touched.join(', ') : 'none'}
          </div>
          {missed.length === 0 && unexpected.length === 0 ? (
            <p className={styles.note}>They agree.</p>
          ) : (
            <p className={styles.mismatch}>
              {missed.length > 0 && `Predicted but never called: ${missed.join(', ')}. `}
              {unexpected.length > 0 && `Called without being predicted: ${unexpected.join(', ')}.`}
            </p>
          )}
        </section>
      )}
    </div>
  );
}
