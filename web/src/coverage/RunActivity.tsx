import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useEffect, useLayoutEffect, useRef, useState, type KeyboardEvent } from 'react';
import type { RunSummary } from '../api/types';
import { useApi } from '../auth/useAuth';
import { StopHint } from '../components/StopHint';
import { describeBudget } from './budget';
import { instant, pct, reasonLabel, RUN_LABELS, stopSentence, usd } from './format';
import { LiveMarker } from './LiveMarker';
import { coverageKeys } from './keys';
import { attemptScope, useRunStream, type RunEnd, type TimelineItem } from './runStream';
import { useRunActivity } from './useRunActivity';
import styles from './CoveragePage.module.css';

/**
 * The Activity button of a file with a run, and the modal it opens: what the agent is doing, live, from the run's
 * AG-UI stream. Closing the modal leaves the run alone; focus goes back to the button.
 */
export function RunActivityButton({ run, canCancel }: { run: RunSummary; canCancel: boolean }) {
  const activity = useRunActivity(run, canCancel);
  return (
    <>
      {activity.button}
      {activity.dialog}
    </>
  );
}

const FOCUSABLE = 'button, [href], input, select, textarea, summary, [tabindex]:not([tabindex="-1"])';

export function RunActivityDialog({ run, canCancel, onClose }: { run: RunSummary; canCancel: boolean; onClose: () => void }) {
  const { summary: live, timeline, ended } = useRunStream(run.id);
  const summary = live ?? run;
  const dialog = useRef<HTMLDivElement>(null);
  const list = useRef<HTMLOListElement>(null);
  const [following, setFollowing] = useState(true);
  // How many entries the user had seen when they scrolled up: anything past that is new to them.
  const [seen, setSeen] = useState(0);
  const unseen = !following && timeline.length > seen;

  useEffect(() => {
    dialog.current?.focus();
  }, []);

  // Keep the newest entry in view, unless the user has scrolled up to read.
  useLayoutEffect(() => {
    const el = list.current;
    if (el && following) el.scrollTop = el.scrollHeight;
  }, [timeline, following]);

  const onScroll = () => {
    const el = list.current;
    if (!el) return;
    const atBottom = el.scrollHeight - el.scrollTop - el.clientHeight < 24;
    if (following && !atBottom) setSeen(timeline.length);
    setFollowing(atBottom);
  };

  const toNewest = () => {
    setFollowing(true);
    if (list.current) list.current.scrollTop = list.current.scrollHeight;
  };

  const agentWorking = !ended && summary.active && summary.state !== 'candidate';
  // Esc stops the run for someone who may (stop-anything); for anyone else it closes the dialog. The close button
  // always only closes it.
  const api = useApi();
  const client = useQueryClient();
  const cancel = useMutation({
    mutationFn: () => api(`/api/coverage/runs/${encodeURIComponent(run.id)}/cancel`, { method: 'POST' }),
    onSuccess: () => void client.invalidateQueries({ queryKey: coverageKeys.all }),
  });
  const stoppable = canCancel && agentWorking;
  const stopping = cancel.isPending || cancel.isSuccess;

  const onKeyDown = (e: KeyboardEvent<HTMLDivElement>) => {
    if (e.key === 'Escape') {
      e.stopPropagation();
      if (stoppable) {
        if (!stopping) cancel.mutate();
        return;
      }
      onClose();
      return;
    }
    if (e.key !== 'Tab' || !dialog.current) return;
    const items = [...dialog.current.querySelectorAll<HTMLElement>(FOCUSABLE)].filter((el) => !el.hasAttribute('disabled'));
    if (items.length === 0) return;
    const first = items[0];
    const last = items[items.length - 1];
    if (e.shiftKey && (document.activeElement === first || document.activeElement === dialog.current)) {
      e.preventDefault();
      last.focus();
    } else if (!e.shiftKey && document.activeElement === last) {
      e.preventDefault();
      first.focus();
    }
  };

  return (
    <div className={styles.backdrop}>
      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby="activity-title"
        className={`${styles.dialog} ${styles.activityDialog}`}
        ref={dialog}
        tabIndex={-1}
        onKeyDown={onKeyDown}
      >
        <h3 id="activity-title" className={styles.dialogTitle}>
          Activity · <span className={styles.activityPath}>{summary.path}</span>
        </h3>
        <RunHeader summary={summary} ended={ended} live={agentWorking} />
        <div className={styles.activityBody}>
          <ol className={styles.timeline} ref={list} onScroll={onScroll} aria-label="Run activity" aria-live="polite">
            {timeline.map((item) => (
              <TimelineRow key={item.id} item={item} />
            ))}
            {timeline.length === 0 &&
              (ended ? (
                <li className={styles.muted}>No activity was recorded for this run.</li>
              ) : (
                <li className={styles.muted}>Waiting for the agent…</li>
              ))}
          </ol>
          {unseen && (
            <button type="button" className={styles.newActivity} onClick={toNewest}>
              New activity ↓
            </button>
          )}
        </div>
        <div className={styles.dialogActions}>
          {stoppable && <StopHint stopping={stopping} className={styles.activityStopHint} />}
          {stoppable && (
            <CancelRun
              pending={cancel.isPending}
              done={cancel.isSuccess}
              failed={cancel.isError}
              onCancel={() => cancel.mutate()}
            />
          )}
          <button type="button" onClick={onClose}>
            Close
          </button>
        </div>
      </div>
    </div>
  );
}

function RunHeader({ summary, ended, live }: { summary: RunSummary; ended: RunEnd | null; live: boolean }) {
  const elapsed = useElapsed(summary.createdAt, live ? null : summary.updatedAt);
  return (
    <dl className={styles.activityHeader} aria-label="Run">
      <div>
        <dt>State</dt>
        <dd>
          {live && <LiveMarker />} <strong>{RUN_LABELS[summary.state] ?? summary.state}</strong>
          {ended?.outcome === 'error' ? (
            <span className={styles.errorText}> ({ended.code})</span>
          ) : (
            !live && summary.reason && <span> · {reasonLabel(summary.reason)}</span>
          )}
        </dd>
      </div>
      <div>
        <dt>Attempt</dt>
        <dd>
          {summary.attempt === 0 ? 'baseline' : `${summary.attempt}/${summary.maxAttempts}`}
          {live && summary.phase ? ` · ${summary.phase}` : ''}
        </dd>
      </div>
      <div>
        <dt>Coverage</dt>
        <dd>
          {summary.lastPct != null ? pct(summary.lastPct) : '—'} of {summary.targetPct}%
        </dd>
      </div>
      <div>
        <dt>Model</dt>
        <dd>{summary.model}</dd>
      </div>
      <div>
        <dt>Budget</dt>
        <dd>{describeBudget(summary.budget)}</dd>
      </div>
      <div>
        <dt>Tokens · cost</dt>
        <dd>
          {summary.tokens.toLocaleString()} · {usd(summary.costUsd)}
        </dd>
      </div>
      <div>
        <dt>Elapsed</dt>
        <dd>{elapsed}</dd>
      </div>
    </dl>
  );
}

function TimelineRow({ item }: { item: TimelineItem }) {
  switch (item.kind) {
    case 'step':
      return <li className={styles.timelineStep}>{item.name}</li>;
    case 'tool':
      return (
        <li className={styles.timelineTool}>
          <span className={styles.toolName}>{item.name}</span>
          {item.path && <code>{item.path}</code>}
          {item.outcome && item.outcome !== 'ok' && <span className={styles.errorText}>{item.outcome}</span>}
          {item.summary && <span className={styles.muted}>— {item.summary}</span>}
        </li>
      );
    case 'text':
    case 'reasoning':
      return (
        <li>
          <details className={styles.timelineText} open={item.kind === 'text'}>
            <summary>
              {item.kind === 'text' ? 'Model' : 'Reasoning'}
              {!item.done && <span className={styles.muted}> · writing…</span>}
            </summary>
            <p>{item.text}</p>
          </details>
        </li>
      );
    case 'attempt': {
      // What the attempt ran (and the whole-suite run that confirmed it); entries from before say nothing.
      const scope = attemptScope(item);
      return (
        <li className={styles.timelineAttempt}>
          <strong>Attempt {item.attempt}</strong>: {item.before != null ? pct(item.before) : '—'} →{' '}
          {item.after != null ? pct(item.after) : 'not measured'} · build {item.build} · {item.tests.passed} passed,{' '}
          {item.tests.failed} failed
          {item.violations > 0 && <span className={styles.errorText}> · {item.violations} rule(s) broken</span>}
          {scope && <span className={styles.muted}> · {scope}</span>}
          {item.errors.length > 0 && (
            <details>
              <summary>{item.errors.length} error(s)</summary>
              <ul>
                {item.errors.map((error, i) => (
                  <li key={i}>
                    <code>{error}</code>
                  </li>
                ))}
              </ul>
            </details>
          )}
        </li>
      );
    }
    case 'notice':
      return <li className={styles.muted}>{item.text}</li>;
    case 'stopped':
      return <li className={styles.timelineStopped}>{stopSentence(item)}</li>;
  }
}

function CancelRun({
  pending,
  done,
  failed,
  onCancel,
}: {
  pending: boolean;
  done: boolean;
  failed: boolean;
  onCancel: () => void;
}) {
  return (
    <>
      {failed && (
        <span className={styles.errorText} role="alert">
          Could not cancel the run.
        </span>
      )}
      <button type="button" onClick={onCancel} disabled={pending || done}>
        {pending ? 'Cancelling…' : 'Cancel run'}
      </button>
    </>
  );
}

/** Minutes and seconds from `from` to `to`, or to now (ticking) while the run is live. */
function useElapsed(from: string, to: string | null): string {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    if (to) return;
    const timer = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(timer);
  }, [to]);
  const start = instant(from);
  const end = to ? instant(to) : now;
  if (Number.isNaN(start) || Number.isNaN(end)) return '—';
  const seconds = Math.max(0, Math.round((end - start) / 1000));
  return `${Math.floor(seconds / 60)}m ${String(seconds % 60).padStart(2, '0')}s`;
}
