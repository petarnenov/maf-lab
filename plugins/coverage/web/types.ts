// Coverage and test-generation contracts.

/** Whether the test agent's card answered just now; reachable means the card answered, not that a run would succeed. */
export interface TestAgentStatus {
  configured: boolean;
  reachable: boolean;
  reason: string | null;
  latencyMs: number;
  checkedAt: string;
}

export interface TestAgentSkill {
  id: string;
  name: string;
  description: string;
  tags: string[];
}

/** What the test agent's public card says. */
export interface TestAgentCard {
  name: string;
  description: string;
  version: string | null;
  skills: TestAgentSkill[];
  endpoint: string | null;
  protocolVersion: string | null;
  requiredScopes: string[];
  streaming: boolean;
  pushNotifications: boolean;
}

export interface TestAgentRunCounts {
  running: number;
  candidates: number;
  accepted: number;
  failed: number;
  other: number;
  total: number;
}

export interface TestAgentRun {
  id: string;
  path: string;
  state: RunState;
  reason: string | null;
  attempt: number;
  maxAttempts: number;
  lastPct: number | null;
  targetPct: number;
  model: string;
  updatedAt: string;
  startedAt: string;
  /** When the run's work ended (candidate or a final state); null while it runs, or when not known. */
  finishedAt: string | null;
  /** Work time in ms: start to end, or start to the api's answer while running; null when not known. */
  durationMs: number | null;
  /** Tokens the run's model calls used (so far, while running). Missing from an api older than show-test-run-cost. */
  tokens?: number;
  /** What the run recorded, in USD: its model calls at the rates it started with — what its cost cap counts. */
  costUsd?: number | null;
  /** The model's rates are the lab's estimate (or the model is no longer on the allowlist). */
  costIsEstimate?: boolean;
  /** The caps chosen at start; a null cap is unlimited. */
  budget?: RunBudget | null;
}

/** The test-generation agent as the agents page shows it. See docs/http-api.md. */
export interface TestAgentOverview {
  status: TestAgentStatus;
  card: TestAgentCard | null;
  /** How the api reaches the agent; never the secret. Null when no agent is configured. */
  connection: { baseUrl: string; clientId: string } | null;
  defaultModel: { tag: string; displayName: string } | null;
  modelsAllowed: number;
  limits: RunLimits;
  /** What a run gets when no budget is chosen: null caps, i.e. unlimited. */
  defaultBudget: { maxTokens: number | null; maxCostUsd: number | null };
  runs: TestAgentRunCounts;
  recent: TestAgentRun[];
}

// Coverage screen (add-coverage-dashboard-and-test-agent).

export type Toolchain = 'dotnet' | 'vitest';

export type RunState =
  | 'submitted'
  | 'working'
  | 'verifying'
  | 'candidate'
  | 'accepted'
  | 'discarded'
  | 'completed_no_change'
  | 'failed'
  | 'canceled'
  | 'verification_failed';

/** A test-generation run as the tree and the file view show it. */
export interface RunSummary {
  id: string;
  path: string;
  state: RunState;
  reason: string | null;
  attempt: number;
  maxAttempts: number;
  lastPct: number | null;
  targetPct: number;
  model: string;
  tokens: number;
  costUsd: number;
  branch: string | null;
  createdAt: string;
  updatedAt: string;
  active: boolean;
  /** The attempt's phase the agent last reported: generating, building, testing or measuring. */
  phase?: string | null;
  /** The caps chosen at start; a missing or null cap is unlimited. */
  budget?: RunBudget | null;
  /** The limits chosen at start (a run from before limits reads as the defaults). */
  limits?: RunLimitsSummary | null;
}

/** The limits a run has. */
export interface RunLimitsSummary {
  maxAttempts: number;
  toolRoundsPerAttempt: number;
  testRunsPerAttempt: number;
  /** The run's own deadline; null is the configured one. */
  deadlineMinutes?: number | null;
  maxSuspectedBugs?: number;
}

/** The limits a start request may carry; one left out takes its default. */
export interface RunLimitsInput {
  maxAttempts?: number;
  toolRoundsPerAttempt?: number;
  testRunsPerAttempt?: number;
  deadlineMinutes?: number;
  maxSuspectedBugs?: number;
}

/** A run's caps; null is unlimited. */
export interface RunBudget {
  maxTokens: number | null;
  maxCostUsd: number | null;
}

export interface CandidateCoverage {
  runId: string;
  pct: number;
  linesCovered: number;
  linesTotal: number;
}

export interface CoverageTreeFile {
  path: string;
  toolchain: Toolchain | null;
  linesTotal: number;
  linesCovered: number;
  branchesTotal: number;
  branchesCovered: number;
  pct: number;
  threshold: number;
  thresholdIsOverride: boolean;
  belowThreshold: boolean;
  commit: string;
  measuredAt: string;
  candidate: CandidateCoverage | null;
  run: RunSummary | null;
}

export interface CoverageTreeFolder {
  path: string;
  linesTotal: number;
  linesCovered: number;
  pct: number;
  files: number;
  filesBelowThreshold: number;
}

export interface CoverageTree {
  hasSnapshot: boolean;
  defaultThresholdPct: number;
  files: CoverageTreeFile[];
  folders: CoverageTreeFolder[];
}

export type LineStatus = 'covered' | 'uncovered' | 'partial';

export interface CoverageLine {
  line: number;
  hits: number;
  branchesCovered: number;
  branchesTotal: number;
  status: LineStatus;
}

export interface CoverageFileDetail {
  path: string;
  toolchain: Toolchain | null;
  commit: string;
  measuredAt: string;
  dirty: boolean;
  kind: 'official' | 'candidate';
  summary: {
    linesTotal: number;
    linesCovered: number;
    branchesTotal: number;
    branchesCovered: number;
    pct: number;
    threshold: number;
    thresholdIsOverride: boolean;
  };
  source: string;
  lines: CoverageLine[];
  run: RunSummary | null;
}

/**
 * The model-independent parts of a run's estimate for one file. Per attempt: input = fixedInputTokens +
 * inputTokensPerRound × min(tool rounds, typicalRounds); output = outputTokensPerAttempt.
 */
export interface EstimateParts {
  fixedInputTokens: number;
  inputTokensPerRound: number;
  typicalRounds: number;
  outputTokensPerAttempt: number;
}

/** A limit a run may lower: its range and the value a run gets when none is chosen. */
export interface LimitBounds {
  min: number;
  max: number;
  default: number;
}

/** Every limit a run may set, with its bounds and default. */
export interface RunLimits {
  maxAttempts: LimitBounds;
  toolRoundsPerAttempt: LimitBounds;
  testRunsPerAttempt: LimitBounds;
  deadlineMinutes: LimitBounds;
  maxSuspectedBugs: LimitBounds;
}

/** One model the test agent may use, as the server's allowlist has it. */
export interface AgentModel {
  tag: string;
  displayName: string;
  inputPerMTok: number;
  outputPerMTok: number;
  bestFor: string;
  isDefault: boolean;
  priceIsEstimate: boolean;
  available: boolean;
  unavailableReason: string | null;
}

export interface AgentModels {
  models: AgentModel[];
  limits: RunLimits;
  /** Null when the file cannot be read: no estimate is shown. */
  estimate: EstimateParts | null;
}

export interface SuspectedBug {
  testFile: string;
  test: string;
  title: string;
  description: string;
  expected: string;
  actual: string;
  failure: string;
}

export interface TestGenReport {
  goalReached: boolean;
  stopReason: string;
  target: number;
  baseline: number | null;
  final: number | null;
  attempts: { n: number; before: number | null; after: number | null; build: string }[];
  usage: { inputTokens: number; outputTokens: number; estimatedCostUsd: number };
  diff: string;
  suspectedBugs?: SuspectedBug[] | null;
  /** How the api's verification run was obtained; absent on runs verified before it was recorded. */
  verification?: VerificationRun | null;
}

/**
 * The api's verification run: its scope, counts and coverage, and, when the runner answered with the result it had
 * computed for the agent's whole-suite confirmation of the same diff, which job computed it.
 */
export interface VerificationRun {
  scope: string;
  tests: { passed: number; failed: number; skipped: number };
  pct: number | null;
  reusedFrom?: { jobId: string; completedAt: string } | null;
}

export interface RunIssue {
  testKey: string;
  title: string;
  number: number | null;
  url: string | null;
}

export interface RunDetail {
  run: RunSummary;
  report: TestGenReport | null;
  issues: RunIssue[];
}

export interface RunDecision {
  run: RunSummary;
  gitHubProblems: string[];
}
