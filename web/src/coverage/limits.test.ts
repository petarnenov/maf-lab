import { describe, expect, it } from 'vitest';
import type { RunLimits } from '../api/types';
import { duration } from './format';
import { defaultLimits, estimateRun, parseLimits } from './limits';

export const runLimits: RunLimits = {
  maxAttempts: { min: 1, max: 10, default: 10 },
  toolRoundsPerAttempt: { min: 1, max: 40, default: 40 },
  testRunsPerAttempt: { min: 0, max: 2, default: 2 },
  deadlineMinutes: { min: 10, max: 120, default: 120 },
  maxSuspectedBugs: { min: 0, max: 3, default: 3 },
};

/** The api's parts for a 3 745-byte file: 43 000 + 4 × 937 file tokens. */
export const parts = { fixedInputTokens: 46_748, inputTokensPerRound: 4_000, typicalRounds: 20, outputTokensPerAttempt: 6_000 };
const glm = { inputPerMTok: 0.6, outputPerMTok: 2.2 };

describe('limits', () => {
  it('fills the fields with the defaults, which parse', () => {
    const input = defaultLimits(runLimits);
    expect(input).toEqual({
      maxAttempts: '10',
      toolRoundsPerAttempt: '40',
      testRunsPerAttempt: '2',
      deadlineMinutes: '120',
      maxSuspectedBugs: '3',
    });
    expect(parseLimits(input, runLimits)).toEqual({
      limits: { maxAttempts: 10, toolRoundsPerAttempt: 40, testRunsPerAttempt: 2, deadlineMinutes: 120, maxSuspectedBugs: 3 },
      errors: {},
    });
  });

  it('refuses a value outside its bounds, not whole, or empty, and says the bounds', () => {
    const base = defaultLimits(runLimits);
    expect(parseLimits({ ...base, toolRoundsPerAttempt: '50' }, runLimits).errors.toolRoundsPerAttempt).toBe(
      'Enter a whole number from 1 to 40.',
    );
    expect(parseLimits({ ...base, maxAttempts: '2.5' }, runLimits).errors.maxAttempts).toBeDefined();
    expect(parseLimits({ ...base, maxAttempts: '' }, runLimits).limits).toBeNull();
    expect(parseLimits({ ...base, testRunsPerAttempt: '0' }, runLimits).errors).toEqual({});
    expect(parseLimits({ ...base, deadlineMinutes: '5' }, runLimits).errors.deadlineMinutes).toBe(
      'Enter a whole number from 10 to 120.',
    );
    expect(parseLimits({ ...base, maxSuspectedBugs: '0' }, runLimits).errors).toEqual({});
    expect(parseLimits({ ...base, maxSuspectedBugs: '4' }, runLimits).errors.maxSuspectedBugs).toBeDefined();
  });

  it('estimates as the api does: the example pinned in RunLimitsTests', () => {
    const estimate = estimateRun(parts, glm, 1, 40);
    expect(estimate.inputTokens).toBe(126_748);
    expect(estimate.perAttemptUsd).toBe(0.0892);
    expect(estimateRun(parts, glm, 10, 40).costUsd).toBeCloseTo(0.8925, 4);
  });

  it('follows the attempts, and fewer rounds than typical', () => {
    expect(estimateRun(parts, glm, 4, 40).tokens).toBe((estimateRun(parts, glm, 10, 40).tokens / 10) * 4);
    expect(estimateRun(parts, glm, 10, 10).costUsd).toBeLessThan(estimateRun(parts, glm, 10, 40).costUsd);
    expect(estimateRun(parts, glm, 10, 40).costUsd).toBe(estimateRun(parts, glm, 10, 20).costUsd);
  });
});

describe('duration', () => {
  it('reads minutes as hours and minutes', () => {
    expect([duration(120), duration(90), duration(45)]).toEqual(['2 h', '1 h 30 min', '45 min']);
  });
});
