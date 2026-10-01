import type { TestAgentOverview } from '../api/types';

/** The test agent as the api reports it when its card answered. */
export const reachableAgent: TestAgentOverview = {
  status: {
    configured: true,
    reachable: true,
    reason: null,
    latencyMs: 12,
    checkedAt: '2026-10-01T09:00:00Z',
  },
  card: {
    name: 'maf-lab test agent',
    description:
      'Writes tests for one source file of the maf-lab repository until its line coverage reaches a target.',
    version: '1.0.0',
    skills: [
      {
        id: 'generate-tests',
        name: 'Generate tests for one file',
        description: 'For: raising the line coverage of one file in this repository to a target.',
        tags: ['tests', 'coverage', 'long-running'],
      },
    ],
    endpoint: 'http://test-agent:8080/a2a',
    protocolVersion: '1.0',
    requiredScopes: ['a2a.testgen.run'],
    streaming: true,
    pushNotifications: true,
  },
  connection: { baseUrl: 'http://test-agent:8080', clientId: 'maf-lab-assistant' },
  defaultModel: { tag: 'glm-5.3:cloud', displayName: 'GLM 5.3' },
  modelsAllowed: 3,
  limits: {
    maxAttempts: { min: 1, max: 10, default: 10 },
    toolRoundsPerAttempt: { min: 1, max: 40, default: 40 },
    testRunsPerAttempt: { min: 0, max: 2, default: 2 },
    deadlineMinutes: { min: 10, max: 120, default: 120 },
    maxSuspectedBugs: { min: 0, max: 3, default: 3 },
  },
  defaultBudget: { maxTokens: null, maxCostUsd: null },
  runs: { running: 1, candidates: 1, accepted: 2, failed: 1, other: 0, total: 5 },
  recent: [
    {
      id: 'r_1',
      path: 'src/Maf.Lab.Api/Coverage/CoverageTree.cs',
      state: 'working',
      reason: null,
      attempt: 3,
      maxAttempts: 10,
      lastPct: 72.4,
      targetPct: 85,
      model: 'glm-5.3:cloud',
      updatedAt: '2026-10-01T08:59:00Z',
      startedAt: '2026-10-01T08:58:18Z',
      finishedAt: null,
      durationMs: 42_000,
      tokens: 300_000,
      costUsd: 0.0421,
      costIsEstimate: true,
      budget: { maxTokens: null, maxCostUsd: null },
    },
    {
      id: 'r_2',
      path: 'src/Maf.Lab.Api/Coverage/CostEstimator.cs',
      state: 'failed',
      reason: 'deadline',
      attempt: 4,
      maxAttempts: 4,
      lastPct: 60,
      targetPct: 90,
      model: 'glm-5.3:cloud',
      updatedAt: '2026-10-01T07:00:00Z',
      startedAt: '2026-10-01T06:56:55Z',
      finishedAt: '2026-10-01T07:00:00Z',
      durationMs: 185_000,
      tokens: 2_760_003,
      costUsd: 0.291,
      costIsEstimate: true,
      budget: { maxTokens: null, maxCostUsd: 0.5 },
    },
    {
      id: 'r_3',
      path: 'web/src/coverage/format.ts',
      state: 'completed_no_change',
      reason: 'attempts',
      attempt: 10,
      maxAttempts: 10,
      lastPct: 50,
      targetPct: 80,
      model: 'kimi-k3:cloud',
      updatedAt: '2026-10-01T06:00:00Z',
      startedAt: '2026-10-01T05:00:00Z',
      finishedAt: null,
      durationMs: null,
      // A run as an api from before show-test-run-cost lists it: no cost fields.
    },
  ],
};

/** The same agent when its card did not answer. */
export const unreachableAgent: TestAgentOverview = {
  ...reachableAgent,
  status: {
    configured: true,
    reachable: false,
    reason: 'No answer within 2 s.',
    latencyMs: 2000,
    checkedAt: '2026-10-01T09:00:00Z',
  },
  card: null,
};

/** No test agent configured, and no run yet. */
export const unconfiguredAgent: TestAgentOverview = {
  ...reachableAgent,
  status: {
    configured: false,
    reachable: false,
    reason: 'No test agent is configured.',
    latencyMs: 0,
    checkedAt: '2026-10-01T09:00:00Z',
  },
  card: null,
  connection: null,
  runs: { running: 0, candidates: 0, accepted: 0, failed: 0, other: 0, total: 0 },
  recent: [],
};
