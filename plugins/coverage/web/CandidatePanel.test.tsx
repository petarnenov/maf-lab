import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import type { RunDetail, RunSummary } from './types';
import { jsonResponse, makeSession, renderWithProviders } from '@maf/testing';
import { CoveragePage } from './CoveragePage';
import { detail, stubCoverageApi } from './CoveragePage.test';
import { summarizeDiff } from './diffSummary';
import { sampleTree } from './treeModel.test';

const candidate: RunSummary = {
  id: 'r_1',
  path: 'src/Lab/Beta.cs',
  state: 'candidate',
  reason: null,
  attempt: 2,
  maxAttempts: 5,
  lastPct: 86,
  targetPct: 85,
  model: 'glm-5.3:cloud',
  tokens: 1000,
  costUsd: 0.09,
  branch: 'test-agent/src-lab-beta-cs-r_1',
  createdAt: '',
  updatedAt: '',
  active: true,
};

const diff = [
  'diff --git a/tests/Lab.Tests/BetaTests.cs b/tests/Lab.Tests/BetaTests.cs',
  'new file mode 100644',
  '--- /dev/null',
  '+++ b/tests/Lab.Tests/BetaTests.cs',
  '@@ -0,0 +1,3 @@',
  '+a',
  '+b',
  '+c',
  '',
].join('\n');

const runDetail: RunDetail = {
  run: candidate,
  report: {
    goalReached: true,
    stopReason: 'target',
    target: 85,
    baseline: 62,
    final: 86,
    attempts: [],
    usage: { inputTokens: 1, outputTokens: 1, estimatedCostUsd: 0.09 },
    diff,
    suspectedBugs: [
      {
        testFile: 'tests/Lab.Tests/BetaTests.cs',
        test: 'EmptyIsFull',
        title: 'Pct of an empty file is 0',
        description: 'd',
        expected: '100',
        actual: '0',
        failure: 'f',
      },
    ],
  },
  issues: [
    {
      testKey: 'tests/Lab.Tests/BetaTests.cs::EmptyIsFull',
      title: 'Pct of an empty file is 0',
      number: 42,
      url: 'https://github.com/owner/repo/issues/42',
    },
  ],
};

function open(decision: (method: string) => Response, role: 'TENANT_ADMIN' | 'USER' = 'TENANT_ADMIN', shown: RunDetail = runDetail) {
  const calls = stubCoverageApi({
    '/api/coverage/tree': () => jsonResponse(sampleTree),
    '/api/coverage/files': () => jsonResponse(detail({ run: candidate })),
    '/api/coverage/runs/r_1/accept': decision,
    '/api/coverage/runs/r_1/discard': decision,
    '/api/coverage/runs/r_1': () => jsonResponse(shown),
  });
  renderWithProviders(<CoveragePage />, {
    route: '/coverage?file=src%2FLab%2FBeta.cs',
    session: makeSession(role),
  });
  return calls;
}

describe('candidate panel', () => {
  it('shows what the run would change, the measured coverage and its bugs with their issues', async () => {
    open(() => jsonResponse({}));

    const panel = await screen.findByRole('region', { name: 'Candidate' });
    expect(panel).toHaveTextContent('86.0%');
    expect(panel).toHaveTextContent('test-agent/src-lab-beta-cs-r_1');
    expect(await within(panel).findByText('tests/Lab.Tests/BetaTests.cs')).toBeInTheDocument();
    expect(panel).toHaveTextContent('+3');
    expect(within(panel).getByRole('link', { name: 'Issue #42' })).toHaveAttribute(
      'href',
      'https://github.com/owner/repo/issues/42',
    );
  });

  it('merges after a confirmation', async () => {
    const calls = open(() => jsonResponse({ run: { ...candidate, state: 'accepted' }, gitHubProblems: [] }));

    const panel = await screen.findByRole('region', { name: 'Candidate' });
    await userEvent.click(within(panel).getByRole('button', { name: 'Accept' }));
    await userEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Merge' }));

    expect(calls.some((c) => c.method === 'POST' && c.url.endsWith('/accept'))).toBe(true);
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it.each([
    ['merge_conflict', 'The tests conflict with main; the run stays a candidate.'],
    [
      'main_dirty',
      'Main is checked out with uncommitted changes; commit or stash them, then accept again. Nothing was written.',
    ],
  ])('says why an accept was refused (%s)', async (type, detailText) => {
    open(() => jsonResponse({ type, title: 'Not merged', detail: detailText }, 409));

    const panel = await screen.findByRole('region', { name: 'Candidate' });
    await userEvent.click(within(panel).getByRole('button', { name: 'Accept' }));
    await userEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Merge' }));

    expect(await within(panel).findByRole('alert')).toHaveTextContent(detailText);
  });

  it('offers no decision to someone who is not an admin', async () => {
    open(() => jsonResponse({}), 'USER');

    const panel = await screen.findByRole('region', { name: 'Candidate' });
    expect(within(panel).queryByRole('button', { name: 'Accept' })).toBeNull();
  });

  it('says when verification reused the runner result computed for the agent confirmation', async () => {
    open(() => jsonResponse({}), 'TENANT_ADMIN', {
      ...runDetail,
      report: {
        ...runDetail.report!,
        verification: {
          scope: 'all',
          tests: { passed: 1219, failed: 0, skipped: 2 },
          pct: 86,
          reusedFrom: { jobId: 'job_1', completedAt: '2026-10-01T10:00:00Z' },
        },
      },
    });

    const panel = await screen.findByRole('region', { name: 'Candidate' });
    expect(await within(panel).findByText(/Whole suite: 1,221 tests, 1,219 passed\./)).toHaveTextContent(
      "verification reused that result",
    );
  });

  it('says nothing about verification for a run verified before it was recorded', async () => {
    open(() => jsonResponse({}));

    const panel = await screen.findByRole('region', { name: 'Candidate' });
    expect(await within(panel).findByText('tests/Lab.Tests/BetaTests.cs')).toBeInTheDocument();
    expect(panel).not.toHaveTextContent('Whole suite');
  });

  it('counts each file of a diff', () => {
    expect(summarizeDiff(diff)).toEqual([{ path: 'tests/Lab.Tests/BetaTests.cs', added: 3, removed: 0 }]);
  });
});
