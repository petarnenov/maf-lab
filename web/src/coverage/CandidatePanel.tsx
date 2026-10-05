import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { ApiError } from '../api/client';
import type { RunDecision, RunDetail, RunSummary, VerificationRun } from '../api/types';
import { useApi } from '../auth/useAuth';
import { summarizeDiff } from './diffSummary';
import { pct } from './format';
import { coverageKeys } from './keys';
import styles from './CoveragePage.module.css';

type Decision = 'accept' | 'discard';

/**
 * A verified run waiting for a person: what it would change, what coverage it measured, the bugs it found with their
 * issues, and — for an admin — Accept (merge into main) or Discard. A refusal says why, in words.
 */
export function CandidatePanel({ run, canDecide }: { run: RunSummary; canDecide: boolean }) {
  const api = useApi();
  const client = useQueryClient();
  const [confirming, setConfirming] = useState<Decision | null>(null);
  const detail = useQuery({
    queryKey: [...coverageKeys.runs(run.path), run.id],
    queryFn: ({ signal }) => api<RunDetail>(`/api/coverage/runs/${encodeURIComponent(run.id)}`, { signal }),
  });
  const decide = useMutation({
    mutationFn: (decision: Decision) =>
      api<RunDecision>(`/api/coverage/runs/${encodeURIComponent(run.id)}/${decision}`, { method: 'POST' }),
    onSuccess: () => {
      setConfirming(null);
      void client.invalidateQueries({ queryKey: coverageKeys.all });
    },
    onError: () => setConfirming(null),
  });

  const report = detail.data?.report;
  const files = report ? summarizeDiff(report.diff) : [];
  const bugs = report?.suspectedBugs ?? [];
  const issueFor = (file: string, test: string) =>
    detail.data?.issues.find((i) => i.testKey === `${file}::${test}`);

  return (
    <section className={styles.candidatePanel} aria-label="Candidate">
      <h3 className={styles.panelTitle}>
        Candidate: {pct(run.lastPct ?? 0)} <span className={styles.muted}>(target {run.targetPct}%)</span>
      </h3>
      <p className={styles.muted}>
        Verified by the lab's own test run. On branch <code>{run.branch}</code>.
      </p>
      {report?.verification && <p className={styles.muted}>{describeVerification(report.verification)}</p>}
      {detail.isError && (
        <p className={styles.errorText} role="alert">
          Could not load the run's report.
        </p>
      )}
      {files.length > 0 && (
        <ul className={styles.diffFiles}>
          {files.map((f) => (
            <li key={f.path}>
              <code>{f.path}</code> <span className={styles.added}>+{f.added}</span>{' '}
              <span className={styles.removed}>−{f.removed}</span>
            </li>
          ))}
        </ul>
      )}
      {bugs.length > 0 && (
        <div>
          <h4 className={styles.panelSubtitle}>Suspected bugs</h4>
          <ul className={styles.bugs}>
            {bugs.map((b) => {
              const issue = issueFor(b.testFile, b.test);
              return (
                <li key={`${b.testFile}::${b.test}`}>
                  <strong>{b.title}</strong> — expected {b.expected}, got {b.actual}.{' '}
                  {issue?.url ? (
                    <a href={issue.url} target="_blank" rel="noreferrer">
                      Issue #{issue.number}
                    </a>
                  ) : (
                    <span className={styles.muted}>No issue was opened.</span>
                  )}
                </li>
              );
            })}
          </ul>
        </div>
      )}
      {decide.isError && (
        <p className={styles.errorText} role="alert">
          {decisionError(decide.error)}
        </p>
      )}
      {decide.data && decide.data.gitHubProblems.length > 0 && (
        <p className={styles.errorText} role="alert">
          {decide.data.gitHubProblems.join(' ')}
        </p>
      )}
      {canDecide && (
        <div className={styles.decisions}>
          <button type="button" className={styles.primary} onClick={() => setConfirming('accept')} disabled={decide.isPending}>
            Accept
          </button>
          <button type="button" onClick={() => setConfirming('discard')} disabled={decide.isPending}>
            Discard
          </button>
        </div>
      )}
      {confirming && (
        <div className={styles.backdrop}>
          <div role="dialog" aria-modal="true" aria-labelledby="decide-title" className={styles.dialog}>
            <h3 id="decide-title" className={styles.dialogTitle}>
              {confirming === 'accept' ? 'Merge these tests into main?' : 'Discard these tests?'}
            </h3>
            <p>
              {confirming === 'accept'
                ? `The branch ${run.branch} is merged into main, and ${pct(run.lastPct ?? 0)} becomes this file's coverage.`
                : 'The branch is deleted, and any issue this run opened is closed with a comment.'}
            </p>
            <div className={styles.dialogActions}>
              <button type="button" onClick={() => setConfirming(null)} disabled={decide.isPending}>
                Cancel
              </button>
              <button
                type="button"
                className={styles.primary}
                disabled={decide.isPending}
                onClick={() => decide.mutate(confirming)}
              >
                {confirming === 'accept' ? 'Merge' : 'Discard'}
              </button>
            </div>
          </div>
        </div>
      )}
    </section>
  );
}

/** Why a decision was refused: the server's own words for a refusal it explains, a plain message otherwise. */
function decisionError(error: unknown): string {
  if (error instanceof ApiError && error.status === 409) return error.message;
  return 'Could not complete that. Nothing was changed.';
}

/** The verification run in a sentence: what it ran, and whether the runner reused the agent's confirmation for it. */
function describeVerification(v: VerificationRun): string {
  const tests = v.tests.passed + v.tests.failed + v.tests.skipped;
  const ran = `${v.scope === 'related' ? 'Related tests' : 'Whole suite'}: ${tests.toLocaleString()} tests, ${v.tests.passed.toLocaleString()} passed.`;
  return v.reusedFrom
    ? `${ran} The runner had already run this exact diff for the agent's confirmation, so verification reused that result.`
    : ran;
}
