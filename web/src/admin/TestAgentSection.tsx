import { useEffect, useState, type ReactNode } from 'react';
import { Link } from 'react-router';
import type { LimitBounds, TestAgentOverview, TestAgentRun } from '../api/types';
import styles from '../components/Page.module.css';
import { Progress } from '../components/Progress';
import { duration, elapsed, pct, reasonLabel, RUN_LABELS } from '../coverage/format';
import { formatDate } from '../evals/format';
import own from './TestAgentSection.module.css';
import { useTestAgentOverview } from './testAgentOverview';

const coverageOf = (file: string) => `/coverage?file=${encodeURIComponent(file)}`;

const bounds = (b: LimitBounds, format: (n: number) => string = String) =>
  `${format(b.default)} (${format(b.min)}–${format(b.max)})`;

/**
 * The test-generation agent, as the api knows it: whether its card answers, what the card says, what a run gets by
 * default, and what its runs have done. Read-only; the browser never talks to the agent, which is internal-network only.
 */
export function TestAgentSection() {
  const overview = useTestAgentOverview();

  return (
    <section className={own.section} aria-labelledby="test-agent-heading" data-testid="test-agent">
      <h2 id="test-agent-heading">Test-generation agent</h2>
      {overview.isPending ? (
        <Progress label="Checking the test agent…" />
      ) : overview.error ? (
        <p role="alert" className={styles.error}>
          The test agent overview could not be loaded.
        </p>
      ) : (
        <Overview data={overview.data} answeredAt={overview.dataUpdatedAt} />
      )}
    </section>
  );
}

/** A run the api reports as still running: no end yet, and a duration up to the api's answer. */
const isRunning = (r: TestAgentRun) => r.finishedAt == null && r.durationMs != null;

/** Now, ticking once a second while `live`; still otherwise, so a page with no running run sets no timer. */
function useNow(live: boolean): number {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    if (!live) return;
    const timer = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(timer);
  }, [live]);
  return now;
}

function Overview({ data, answeredAt }: { data: TestAgentOverview; answeredAt: number }) {
  const { status, card, connection, defaultModel, limits, defaultBudget, runs, recent } = data;
  const unlimited = defaultBudget.maxTokens == null && defaultBudget.maxCostUsd == null;
  // A running run's duration counts on from the api's value by the time since that answer arrived, so the browser's
  // clock never enters it; the next refresh replaces it with the api's value.
  const now = useNow(recent.some(isRunning));
  const sinceAnswer = Math.max(0, now - answeredAt);

  return (
    <>
      <p className={own.status} data-testid="test-agent-status">
        {!status.configured ? (
          <span className={styles.muted}>Not configured — {status.reason}</span>
        ) : status.reachable ? (
          <>
            <span className={styles.pass}>Reachable</span>
            <span className={styles.muted}>
              · card answered in {status.latencyMs} ms · checked {formatDate(status.checkedAt)}
            </span>
          </>
        ) : (
          <>
            <span className={styles.fail}>Unreachable</span>
            <span>— {status.reason}</span>
            <span className={styles.muted}>· checked {formatDate(status.checkedAt)}</span>
          </>
        )}
      </p>

      <div className={styles.cards} data-testid="test-agent-counts">
        <Count label="Running now" value={runs.running} />
        <Count label="Candidates" value={runs.candidates} />
        <Count label="Accepted" value={runs.accepted} />
        <Count label="Failed" value={runs.failed} />
        <Count label="Ended otherwise" value={runs.other} />
      </div>
      <p>
        <Link className={own.link} to="/coverage">
          Open Coverage →
        </Link>{' '}
        <span className={styles.muted}>to start, follow, accept or discard a run.</span>
      </p>

      <h3 className={styles.subheading}>Agent card</h3>
      <div className={styles.scroll}>
        <table className={`${styles.table} ${own.facts}`} data-testid="test-agent-card">
          <tbody>
            {card ? (
              <>
                <Fact name="Name">{card.name}</Fact>
                {card.version && <Fact name="Version">{card.version}</Fact>}
                <Fact name="Description">{card.description}</Fact>
                {card.skills.map((s) => (
                  <Fact key={s.id} name="Skill">
                    <strong>{s.name}</strong> <code className={styles.mono}>{s.id}</code>
                    <div className={styles.muted}>{s.description}</div>
                    <div>
                      {s.tags.map((t) => (
                        <span key={t} className={styles.tag}>
                          {t}
                        </span>
                      ))}
                    </div>
                  </Fact>
                ))}
                {card.endpoint && (
                  <Fact name="Endpoint (internal network)">
                    <code className={styles.mono}>{card.endpoint}</code>
                    {card.protocolVersion && (
                      <span className={styles.muted}> · A2A {card.protocolVersion}</span>
                    )}
                  </Fact>
                )}
                <Fact name="Required scope">
                  {card.requiredScopes.length > 0 ? (
                    <code className={styles.mono}>{card.requiredScopes.join(', ')}</code>
                  ) : (
                    <span className={styles.muted}>none stated</span>
                  )}
                </Fact>
                <Fact name="Capabilities">
                  {[card.streaming && 'streaming', card.pushNotifications && 'push notifications']
                    .filter(Boolean)
                    .join(', ') || 'none'}
                </Fact>
              </>
            ) : (
              <Fact name="Card">
                <span className={styles.muted}>
                  {status.configured ? 'The card could not be read.' : 'No agent to ask.'}
                </span>
              </Fact>
            )}
            {connection && (
              <Fact name="The api signs in as">
                <code className={styles.mono}>{connection.clientId}</code>
                <span className={styles.muted}> at </span>
                <code className={styles.mono}>{connection.baseUrl}</code>
              </Fact>
            )}
          </tbody>
        </table>
      </div>

      <h3 className={styles.subheading}>What a run gets by default</h3>
      <div className={styles.scroll}>
        <table className={`${styles.table} ${own.facts}`} data-testid="test-agent-defaults">
          <tbody>
            <Fact name="Model">
              {defaultModel ? (
                <>
                  {defaultModel.displayName} <code className={styles.mono}>{defaultModel.tag}</code>
                  <span className={styles.muted}> · one of {data.modelsAllowed} allowed</span>
                </>
              ) : (
                <span className={styles.muted}>no model is allowed</span>
              )}
            </Fact>
            <Fact name="Attempts">{bounds(limits.maxAttempts)}</Fact>
            <Fact name="Tool rounds per attempt">{bounds(limits.toolRoundsPerAttempt)}</Fact>
            <Fact name="Test runs per attempt">{bounds(limits.testRunsPerAttempt)}</Fact>
            <Fact name="Suspected bugs">{bounds(limits.maxSuspectedBugs)}</Fact>
            <Fact name="Deadline">{bounds(limits.deadlineMinutes, duration)}</Fact>
            <Fact name="Budget">
              {unlimited
                ? 'None — a run is limited by its attempts and its deadline'
                : [
                    defaultBudget.maxTokens != null && `${defaultBudget.maxTokens} tokens`,
                    defaultBudget.maxCostUsd != null && `$${defaultBudget.maxCostUsd}`,
                  ]
                    .filter(Boolean)
                    .join(', ')}
            </Fact>
          </tbody>
        </table>
      </div>
      <p className={styles.muted}>Default (lowest–highest a run may choose).</p>

      <h3 className={styles.subheading}>Recent runs</h3>
      {recent.length === 0 ? (
        <p className={styles.muted} data-testid="test-agent-no-runs">
          No run has been started yet.
        </p>
      ) : (
        <div className={styles.scroll}>
          <table className={styles.table} data-testid="test-agent-runs">
            <thead>
              <tr>
                <th>File</th>
                <th>State</th>
                <th>Attempt</th>
                <th>Coverage</th>
                <th>Reason</th>
                <th>Model</th>
                <th>Duration</th>
                <th>When</th>
              </tr>
            </thead>
            <tbody>
              {recent.map((r) => (
                <tr key={r.id} data-state={r.state}>
                  <td>
                    <Link className={own.link} to={coverageOf(r.path)}>
                      <code className={styles.mono}>{r.path}</code>
                    </Link>
                  </td>
                  <td>{RUN_LABELS[r.state] ?? r.state}</td>
                  <td>
                    {r.attempt > 0 ? r.attempt : '–'}/{r.maxAttempts}
                  </td>
                  <td>
                    {r.lastPct != null ? pct(r.lastPct) : '–'} → {r.targetPct}%
                  </td>
                  <td>{reasonLabel(r.reason) ?? '—'}</td>
                  <td>
                    <code className={styles.mono}>{r.model}</code>
                  </td>
                  <td data-testid="test-agent-run-duration">
                    {isRunning(r) ? (
                      <span title="Still running: counted from the last refresh">
                        {elapsed(r.durationMs! + sinceAnswer)}{' '}
                        <span className={styles.muted}>so far</span>
                      </span>
                    ) : (
                      elapsed(r.durationMs)
                    )}
                  </td>
                  <td>{formatDate(r.updatedAt)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </>
  );
}

function Count({ label, value }: { label: string; value: number }) {
  return (
    <div className={`${styles.card} ${own.count}`}>
      <p className={styles.muted}>{label}</p>
      <p className={styles.big}>{value}</p>
    </div>
  );
}

function Fact({ name, children }: { name: string; children: ReactNode }) {
  return (
    <tr>
      <th scope="row">{name}</th>
      <td>{children}</td>
    </tr>
  );
}
