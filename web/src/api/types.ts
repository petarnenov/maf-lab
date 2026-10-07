// Mirrors the DTOs in src/Maf.Lab.Domain (camelCase JSON). See docs/http-api.md.

/** The core's roles. Domain roles (billing's advisor, ops) are claims the core and this shell do not read. */
export type Role = 'TENANT_ADMIN' | 'USER' | 'READ_ONLY';

export interface DevUser {
  userId: string;
  tenantId: string;
  role: Role;
  label: string;
}

/** The dev issuer adds a persona's domain claims itself; the shell sends only the core shape. */
export interface DevTokenRequest {
  userId: string;
  tenantId: string;
  role: Role;
}

export interface DevTokenResponse {
  token: string;
  expiresAt: string;
}

export interface Me {
  userId: string;
  tenantId: string;
  role: Role;
}

// ---- Chat ----
// The chat reads the protocol's own events (@ag-ui/core) through CopilotKit; these are the screen's shapes.

export interface SourceRef {
  docId: string;
  sectionPath: string;
  sourcePath: string;
  snippet: string;
  /** "code" for a place in the repository (add-codebase-domain); absent for documentation. */
  kind?: string;
  startLine?: number;
  endLine?: number;
  symbol?: string;
  language?: string;
}

/**
 * The JSON Schema 2020-12 annotations a write's flow describes its summary with: display only, never a form. Each
 * property is shown by its `title`, in property order, formatted by `type` and `format`.
 */
export interface SummarySchema {
  type?: string;
  properties?: Record<string, SummaryProperty>;
}

export interface SummaryProperty {
  title?: string;
  description?: string;
  type?: string;
  format?: string;
}

/**
 * A write waiting for a person, whichever plugin's tool proposed it (generalize-write-confirmation), as the run's pause
 * and `GET /api/conversations/{id}/pending` carry it. The summary is the
 * tool's own; the opaque state never reaches the browser — an answer names the write by its id.
 */
export interface PendingWrite {
  writeId: string;
  toolName: string;
  summary: unknown;
  /** The schema the tool's flow describes the summary with; null when no installed plugin has a flow for the tool. */
  summarySchema?: SummarySchema | null;
  question: string;
  expiresAt?: string | null;
}

/**
 * A data card (AG-UI `ACTIVITY_SNAPSHOT`): a typed tool result the chat renders as a table. `activityType` is open —
 * a type this screen does not know renders nothing.
 */
/** The account a conversation is about (add-focus-state): an id only. */
export interface FocusAccount {
  accountId: string;
}

export interface DataCard {
  messageId: string;
  activityType: string;
  content: Record<string, unknown>;
}

export interface ChatRequest {
  conversationId?: string;
  message: string;
}

// ---- Feedback ----

export type FeedbackKind = 'wrong_tool' | 'wrong_document' | 'wrong_answer' | 'wrong_confirmation';

export interface FeedbackRequest {
  conversationId: string;
  turnId: string;
  kind: FeedbackKind;
  comment?: string;
}

export interface FeedbackAccepted {
  feedbackId: string;
}

export type TurnSignal =
  | 'negative_feedback'
  | 'rephrased'
  | 'no_tool_on_how_why'
  | 'zero_retrieval_results'
  | 'long_answer_without_sources'
  | 'guardrail_blocked'
  | 'out_of_scope'
  | 'guardrail_withheld'
  | 'answer_not_grounded'
  | 'answer_not_relevant';

export interface ToolCallRecord {
  toolName: string;
  argumentSummary: string;
  outcome: string;
  sourceCount: number;
  docIds: string[];
  chunkIds: string[];
}

export interface ReviewQueueItem {
  turnId: string;
  conversationId: string;
  userId: string;
  question: string;
  answer: string;
  signals: TurnSignal[];
  toolCalls: ToolCallRecord[];
  feedbackKinds: FeedbackKind[];
  createdAt: string;
  labeled: boolean;
}

export type LabelDataset = 'selection' | 'retrieval' | 'generation';

export interface LabelRequest {
  dataset: LabelDataset;
  expectedTools?: string[] | null;
  relevantChunkIds?: string[] | null;
  referenceAnswer?: string | null;
  expectedDocIds?: string[] | null;
}

// ---- Admin / index ----

export interface StaleDocument {
  docId: string;
  sourcePath: string;
  sourceUpdatedAt: string;
  indexedUpdatedAt: string;
}

/** The billing graph against the source (add-graph-drift); reason is "unreachable" when it could not be read. */
export interface GraphDrift {
  available: boolean;
  reason: string | null;
  outOfSync: number;
  outOfSyncPercent: number;
  missingFromGraph: string[];
  behind: string[];
  notInCorpus: string[];
}

export interface DriftReport {
  totalDocuments: number;
  staleDocuments: number;
  stalePercent: number;
  stale: StaleDocument[];
  missingFromIndex: string[];
  graph?: GraphDrift | null;
}

export interface ModelVersionCount {
  modelVersion: string;
  chunks: number;
}

export type AdminJobState = 'queued' | 'running' | 'succeeded' | 'failed' | 'canceled';

export interface AdminJob {
  jobId: string;
  kind: string;
  state: AdminJobState;
  startedAt: string;
  finishedAt?: string | null;
  summary?: string | null;
}

export interface IndexStatus {
  modelVersions: ModelVersionCount[];
  activeDenseVector: string;
  currentJob?: AdminJob | null;
}

// ---- Evals ----

export interface EvalCaseFailure {
  caseId: string;
  reason: string;
}

export interface EvalVariantResult {
  name: string;
  metrics: Record<string, number>;
  thresholds: Record<string, number>;
  passed: boolean;
  cases: number;
  failures: EvalCaseFailure[];
}

export type MetricStatus = 'Regression' | 'Noise' | 'Improvement' | 'New' | 'Missing';

export interface MetricComparison {
  variant: string;
  metric: string;
  /** The accepted value; null when the baseline does not mention this metric. */
  baseline: number | null;
  value: number | null;
  delta: number | null;
  status: MetricStatus;
}

export interface EvalReportSummary {
  runId: string;
  suite: string;
  startedAt: string;
  passed: boolean;
  variants: EvalVariantResult[];
  /** Absent in reports written before the regression gate existed. */
  comparisons?: MetricComparison[] | null;
}

export interface EvalReport {
  runId: string;
  suite: string;
  startedAt: string;
  finishedAt: string;
  settings: Record<string, string>;
  variants: EvalVariantResult[];
  passed: boolean;
  comparisons?: MetricComparison[] | null;
}

// ---- A conversation reopened (the list is the conversation-history plugin's) ----

/** callId and resultSummary are null for turns stored before they were persisted. */
export interface HistoryToolCall {
  callId?: string | null;
  toolName: string;
  argumentSummary: string;
  outcome: string;
  resultSummary?: string | null;
  sourceCount: number;
}

export interface HistoryTurn {
  turnId: string;
  question: string;
  answer: string;
  createdAt: string;
  toolCalls: HistoryToolCall[];
  sources: SourceRef[];
  feedbackKinds: FeedbackKind[];
  /** The data cards the turn showed; absent or empty for turns stored before cards existed. */
  activities?: DataCard[] | null;
  /** What the model reasoned before it answered; absent or null when it did not. */
  reasoning?: string | null;
  reasoningMs?: number | null;
}

export interface ConversationDetail {
  conversationId: string;
  title: string;
  createdAt: string;
  lastActivityAt: string;
  turns: HistoryTurn[];
  /** The account in focus; absent or null when none (add-focus-state). */
  focus?: FocusAccount | null;
}

// ---- Topology ----

export type NodeHealth = 'Healthy' | 'Degraded' | 'Unreachable' | 'NotProbed';

export interface TopologyInstance {
  name: string;
  address: string | null;
  health: NodeHealth;
  reason: string | null;
}

export interface TopologyNode {
  id: string;
  name: string;
  health: NodeHealth;
  instances: TopologyInstance[];
  facts: Record<string, string>;
  reason: string | null;
}

export interface TopologyEdge {
  from: string;
  to: string;
  label: string | null;
}

export interface TopologyReport {
  generatedAt: string;
  cacheSeconds: number;
  discoveryAvailable: boolean;
  reportedBy: string;
  nodes: TopologyNode[];
  edges: TopologyEdge[];
}

// ---- Compliance ----

// ---- A2A activity (admin) ----

export interface InboundTask {
  taskId: string;
  partnerId: string;
  operation: string;
  state: string;
  createdAt: string;
  updatedAt: string;
  durationMs: number;
  cancellable: boolean;
}

export interface OutboundConsultation {
  taskId: string;
  agent: string;
  outcome: string;
  durationMs: number;
  at: string;
}

export interface PushDelivery {
  taskId: string;
  state: string;
  url: string;
  attempts: number;
  delivered: boolean;
  error?: string | null;
  at: string;
}

export interface A2AActivity {
  inbound: InboundTask[];
  outbound: OutboundConsultation[];
  deliveries: PushDelivery[];
}

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
