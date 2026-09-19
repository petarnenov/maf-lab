import { describe, expect, it } from 'vitest';
import type { AdminJob, AdminJobState } from '../api/types';
import { isJobActive } from './jobs';

const job = (state: AdminJobState): AdminJob => ({
  jobId: 'j',
  kind: 'index',
  state,
  startedAt: '2026-09-19T10:00:00Z',
});

describe('isJobActive', () => {
  it.each<[AdminJobState, boolean]>([
    ['queued', true],
    ['running', true],
    ['succeeded', false],
    ['failed', false],
  ])('%s → active=%s', (state, active) => {
    expect(isJobActive(job(state))).toBe(active);
  });

  it('treats a missing job as inactive', () => {
    expect(isJobActive(null)).toBe(false);
    expect(isJobActive(undefined)).toBe(false);
  });
});
