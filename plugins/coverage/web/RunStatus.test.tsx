import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { RunSummary } from './types';
import { RunStatus } from './RunStatus';

const run = (overrides: Partial<RunSummary>): RunSummary => ({
  id: 'r_1',
  path: 'src/Lab/Beta.cs',
  state: 'working',
  reason: null,
  attempt: 2,
  maxAttempts: 5,
  lastPct: 71.4,
  targetPct: 85,
  model: 'glm-5.3:cloud',
  tokens: 100,
  costUsd: 0.01,
  branch: null,
  createdAt: '',
  updatedAt: '',
  active: true,
  ...overrides,
});

describe('RunStatus', () => {
  it('says which phase an active attempt is in', () => {
    render(<RunStatus run={run({ phase: 'building' })} />);
    expect(screen.getByRole('status', { name: 'Run status' })).toHaveTextContent('Working · attempt 2/5 · building · 71.4%');
  });

  it('leaves the phase out once the run has ended', () => {
    render(<RunStatus run={run({ state: 'failed', active: false, phase: 'measuring' })} />);
    expect(screen.getByRole('status', { name: 'Run status' })).not.toHaveTextContent('measuring');
  });

  it('says why a run stopped, in words', () => {
    render(<RunStatus run={run({ state: 'completed_no_change', reason: 'budget', active: false, lastPct: 0 })} />);
    expect(screen.getByRole('status', { name: 'Run status' })).toHaveTextContent('No change · attempt 2/5 · 0.0% · target 85%');
    expect(screen.getByRole('status', { name: 'Run status' })).toHaveTextContent('· budget');
  });

  it('keeps a reason that is not a stop as the server gave it', () => {
    render(<RunStatus run={run({ state: 'failed', reason: 'deadline', active: false })} />);
    expect(screen.getByRole('status', { name: 'Run status' })).toHaveTextContent('· deadline');
  });
});
