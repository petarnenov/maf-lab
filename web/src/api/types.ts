// Mirrors the DTOs in src/Maf.Lab.Domain (camelCase JSON). See docs/http-api.md.

export type Role = 'FIRM_ADMIN' | 'ADVISOR' | 'OPS' | 'READ_ONLY';

export interface DevUser {
  userId: string;
  firmId: string;
  role: Role;
  advisorIds: string[];
  label: string;
}

export interface DevTokenRequest {
  userId: string;
  firmId: string;
  role: Role;
  advisorIds?: string[];
}

export interface DevTokenResponse {
  token: string;
  expiresAt: string;
}

export interface Me {
  userId: string;
  firmId: string;
  role: Role;
  advisorIds: string[];
}

// ---- Chat / SSE ----
// The AG-UI SDK shapes the wire; ChatStreamEvent remains the reducer's internal form.

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

export interface ToolCallStartedData {
  callId: string;
  toolName: string;
  argumentSummary: string;
}

export interface ToolCallFinishedData {
  callId: string;
  toolName: string;
  resultSummary: string;
  sourceCount: number;
  isError: boolean;
}

/** A fee adjustment waiting for the advisor. `state` is opaque: hand it back, never read it. */
export interface FeeAdjustmentSummary {
  adjustmentId: string;
  accountId: string;
  accountName: string;
  currentFee: number;
  amount: number;
  resultingFee: number;
  currency: string;
  periodStart: string;
  periodEnd: string;
}

export interface ConfirmationRequiredData {
  callId: string;
  toolName: string;
  adjustmentId: string;
  adjustment: FeeAdjustmentSummary;
  question: string;
  state: string;
  /** When the proposal stops being answerable, as the server issued it. */
  expiresAt?: string | null;
}

/** A proposal a conversation is still waiting on, as `GET /api/conversations/{id}/pending` reports it. */
export interface PendingProposal {
  adjustmentId: string;
  adjustment: FeeAdjustmentSummary;
  question: string;
  expiresAt?: string | null;
}

export interface DoneData {
  conversationId: string;
  turnId: string;
  error?: string | null;
}

/**
 * One event as it crossed the AG-UI wire, kept so the monitor can show what the rest of the screen drops.
 * `seq` counts frames of the run from 1 and `atMs` is measured from its first frame. A trace frame carries
 * `traceSeq` and no `payload`: the trace event itself is what the monitor's other tabs already render.
 * `unparsed` holds the raw text of a frame whose JSON could not be read.
 */
export interface AguiFrame {
  seq: number;
  atMs: number;
  /** The protocol event type; the SSE frame's own name when the payload could not be read. */
  type: string;
  /** A custom event's name. */
  name?: string;
  bytes: number;
  /** The sequence number of the trace event a `maf-lab/trace` frame carried. */
  traceSeq?: number;
  payload?: unknown;
  unparsed?: string;
  /** Set by the server when the run's size cap left this frame without its payload. */
  truncated?: boolean;
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

export type ChatStreamEvent =
  | { type: 'text_delta'; data: { text: string } }
  | { type: 'tool_call_started'; data: ToolCallStartedData }
  | { type: 'tool_call_finished'; data: ToolCallFinishedData }
  | { type: 'sources'; data: { sources: SourceRef[] } }
  /** The run's shared state (AG-UI `STATE_SNAPSHOT`): the account in focus (add-focus-state). */
  | { type: 'state'; data: { focus: FocusAccount | null } }
  /** A data card: an AG-UI activity a carded tool result became (add-activity-cards). */
  | { type: 'card'; data: DataCard }
  | { type: 'reasoning_delta'; data: { text: string } }
  /** The model stopped reasoning. It does not close the block — the answer's first text does. */
  | { type: 'reasoning_end' }
  | { type: 'trace'; data: TraceEvent }
  | { type: 'agui_frame'; data: AguiFrame }
  | { type: 'confirmation_required'; data: ConfirmationRequiredData }
  /** The run has begun, in this conversation: the server names it before anything can go wrong. */
  | { type: 'run_started'; data: { conversationId: string } }
  | { type: 'done'; data: DoneData };

// ---- Turn trace (behind the scenes). See docs/trace-events.md. ----

export type TraceKind =
  | 'turn.start'
  | 'intent'
  | 'domain'
  | 'boundary'
  | 'history'
  | 'prompt'
  | 'model.request'
  | 'model.response'
  | 'tool.forced'
  | 'tool.call'
  | 'tool.result'
  | 'retrieval'
  | 'envelope'
  | 'tool.unknown'
  | 'audit'
  | 'adjustment'
  | 'guardrail'
  | 'sources'
  | 'card'
  | 'focus'
  | 'signals'
  | 'memory'
  | 'turn.end';

export interface TraceEvent {
  seq: number;
  atMs: number;
  /** One of TraceKind; unknown kinds are still shown in the timeline. */
  kind: string;
  title: string;
  durationMs?: number | null;
  data: unknown;
  truncated: boolean;
}

export interface TurnTraceDocument {
  turnId: string;
  conversationId: string;
  createdAt: string;
  events: TraceEvent[];
  /** The run's frames, or null for a turn answered before they were kept. */
  aguiFrames?: AguiFrame[] | null;
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

export interface DriftReport {
  totalDocuments: number;
  staleDocuments: number;
  stalePercent: number;
  stale: StaleDocument[];
  missingFromIndex: string[];
}

export interface ModelVersionCount {
  modelVersion: string;
  chunks: number;
}

export type AdminJobState = 'queued' | 'running' | 'succeeded' | 'failed';

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

// ---- Conversation history ----

export interface ConversationSummary {
  conversationId: string;
  title: string;
  createdAt: string;
  lastActivityAt: string;
  turnCount: number;
}

export interface ConversationPage {
  conversations: ConversationSummary[];
  nextCursor: string | null;
}

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
  traceAvailable: boolean;
  /** The data cards the turn showed; absent or empty for turns stored before cards existed. */
  activities?: DataCard[] | null;
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

export interface RenameConversationRequest {
  title: string;
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

export type AuditKind =
  | 'tool'
  | 'conversation.delete'
  | 'compliance.export'
  | 'a2a.request'
  | 'a2a.consultation'
  | 'fee.adjustment';

export interface ChainReport {
  intact: boolean;
  checked: number;
  /** Rows written before chaining began: reported, never rewritten. */
  unchained: number;
  from: string | null;
  to: string | null;
  head: string | null;
  firstBrokenId: number | null;
  reason: string | null;
}

export interface AuditAction {
  id: number;
  at: string;
  principalId: string;
  kind: AuditKind | null;
  action: string;
  /** Identifiers only (key=value); empty when the action carried none. */
  arguments: string;
  outcome: string;
  durationMs: number;
  conversationId: string | null;
  turnId: string | null;
  hash: string | null;
}

export interface ActionPage {
  actions: AuditAction[];
  nextCursor: number | null;
}

export interface ExportManifest {
  firmId: string;
  subjectUserId: string | null;
  from: string;
  to: string;
  generatedAt: string;
  by: string;
  counts: Record<string, number>;
  sha256: string;
  auditChainHead: string | null;
}

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

// ---- Telemetry (what the stack measured about itself). See docs/http-api.md. ----

export interface TelemetrySeries {
  label: string;
  /** Null when the query returned nothing usable for that row. */
  value: number | null;
}

export interface TelemetryPanel {
  id: string;
  title: string;
  unit: string;
  /** Empty when nothing was measured in the window, which reads as no data rather than as zero. */
  series: TelemetrySeries[];
}

export interface TelemetryReport {
  window: string;
  generatedAt: string;
  /** False when the metrics store could not be read; the screen still renders. */
  available: boolean;
  reason: string | null;
  panels: TelemetryPanel[];
  /** Where a turn's trace opens; the trace id is appended to it. */
  traceUrl: string | null;
}

// ---- Intent statistics ----

/** Jev's answer was acted on, answered but overruled by a floor, or never usable. */
export type IntentOutcome = 'used' | 'gated' | 'failed';

export interface IntentStatsSettings {
  model: string;
  minConfidence: number;
  minInDomain: number;
  timeoutSeconds: number;
}

export interface IntentStatsTotals {
  classified: number;
  used: number;
  gated: number;
  failed: number;
  forced: number;
  /** Intent events from an earlier classifier in the window, left out of everything else. */
  excludedEvents: number;
}

/** Counts along the edges of the classification pipeline. */
export interface IntentPipelineCounts {
  classified: number;
  failed: number;
  answered: number;
  unknownChoice: number;
  belowConfidence: number;
  notForcingIntent: number;
  forcingIntent: number;
  outsideDomain: number;
  forced: number;
}

export interface IntentReasonCount {
  outcome: IntentOutcome;
  label: string;
  count: number;
}

export interface IntentChoiceCount {
  /** Jev's raw choice, or 'none' when it gave no answer. */
  choice: string;
  /** The intent the turn proceeded with. */
  intent: string;
  count: number;
}

export interface IntentTimelineBucket {
  start: string;
  used: number;
  gated: number;
  failed: number;
  timedOut: number;
  p50Ms: number | null;
  p90Ms: number | null;
}

export interface IntentProbabilityBin {
  from: number;
  to: number;
  used: number;
  gated: number;
}

export interface IntentPoint {
  confidence: number;
  inDomain: number;
  outcome: IntentOutcome;
  choice: string;
}

export interface IntentMeanProbability {
  intent: string;
  mean: number;
  chosen: number;
}

export interface IntentLatencyBin {
  fromMs: number;
  /** Null for the overflow bin at and past the timeout. */
  toMs: number | null;
  count: number;
}

export interface IntentLatency {
  count: number;
  p50: number | null;
  p90: number | null;
  p99: number | null;
  max: number | null;
  bins: IntentLatencyBin[];
}

export interface IntentStatsReport {
  window: string;
  from: string;
  to: string;
  bucketMinutes: number;
  settings: IntentStatsSettings;
  totals: IntentStatsTotals;
  pipeline: IntentPipelineCounts;
  reasons: IntentReasonCount[];
  choices: IntentChoiceCount[];
  timeline: IntentTimelineBucket[];
  confidence: IntentProbabilityBin[];
  inDomain: IntentProbabilityBin[];
  points: IntentPoint[];
  meanProbabilities: IntentMeanProbability[];
  latency: IntentLatency;
  models: { model: string; count: number }[];
}

// ---- Jev overview (every call site) ----

export interface JevStatsSettings {
  model: string;
  guardEnabled: boolean;
  promptBlockAt: number;
  contentWithholdAt: number;
  crossTenantAt: number;
  relevanceFloor: number | null;
}

export interface JevSiteSummary {
  site: string;
  requests: number;
  unavailable: number;
  p50Ms: number | null;
  p90Ms: number | null;
  /** Calls an open circuit skipped: not requests, not unavailable. Absent from an api older than the breaker. */
  skipped?: number;
}

export interface JevAvailabilityBucket {
  start: string;
  requests: number;
  unavailable: number;
  /** Absent from an api older than the circuit breaker. */
  skipped?: number;
}

export interface JevOverview {
  settings: JevStatsSettings;
  requests: number;
  unavailable: number;
  turns: number;
  requestsPerTurn: number | null;
  sites: JevSiteSummary[];
  timeline: JevAvailabilityBucket[];
  /** Calls an open circuit skipped across every site. Absent from an api older than the breaker. */
  skipped?: number;
}

export interface GuardrailCheckCount {
  check: string;
  total: number;
  pass: number;
  blocked: number;
  withheld: number;
  unscreened: number;
}

export interface GuardrailQuestionCount {
  question: string;
  /** blocked or withheld. */
  decision: string;
  count: number;
}

export interface GuardrailTimelineBucket {
  start: string;
  screened: number;
  blocked: number;
  withheld: number;
  unscreened: number;
}

export interface GuardrailStats {
  checks: GuardrailCheckCount[];
  trippedBy: GuardrailQuestionCount[];
  screened: number;
  blocked: number;
  withheld: number;
  unscreened: number;
  latency: IntentLatency;
  timeline: GuardrailTimelineBucket[];
}

export interface RelevanceMaxBin {
  from: number;
  to: number;
  kept: number;
  gated: number;
}

export interface RelevanceTimelineBucket {
  start: string;
  searches: number;
  gated: number;
  unavailable: number;
}

export interface RelevanceStats {
  floor: number | null;
  searches: number;
  gated: number;
  reranked: number;
  unavailable: number;
  maxHistogram: RelevanceMaxBin[];
  latency: IntentLatency;
  timeline: RelevanceTimelineBucket[];
  /** Judged searches per domain's documentation; absent from an api older than the portfolio domain. */
  byDomain?: RelevanceDomainCount[] | null;
}

export interface RelevanceDomainCount {
  domain: string;
  searches: number;
  gated: number;
  unavailable: number;
}

/** Where Jev placed the turns among the domains, and whether the calls went there. */
export interface DomainStats {
  judged: number;
  billing: number;
  portfolio: number;
  both: number;
  none: number;
  crossed: number;
  withCalls: number;
  agreed: number;
}

/** Jev's check of the final answer, over the window. Numbers only. */
export interface AnswerCheckStats {
  /** Answer checks recorded, checked or not. */
  answers: number;
  /** Answers that got a verdict (pass, uncertain, not relevant, not grounded). */
  checked: number;
  pass: number;
  /**
   * Checked answers in the review band — above both signal floors, below a pass threshold — which raise no signal.
   * Optional: an api from before the band does not send it.
   */
  uncertain?: number;
  /** Checked answers below the relevance floor. */
  notRelevant: number;
  /** Checked answers below the grounding floor; an answer can be below both. */
  notGrounded: number;
  /** No verdict: disabled, no key, sources over the cap, or Jev unavailable. */
  unchecked: number;
  /** Of the unchecked, those Jev was unavailable for. */
  unavailable: number;
  relevantFloor: number | null;
  groundedFloor: number | null;
  latency: IntentLatency;
}

export interface RoutingToolCount {
  tool: string;
  count: number;
}

export interface RoutingReasonCount {
  reason: string;
  count: number;
}

export interface RoutingStats {
  enabled: boolean;
  dataTurns: number;
  routed: number;
  tools: RoutingToolCount[];
  notRoutedReasons: RoutingReasonCount[];
  modelCallsRoutedMedian: number | null;
  modelCallsUnroutedMedian: number | null;
  latency: IntentLatency;
}

export interface JevStatsReport {
  window: string;
  from: string;
  to: string;
  bucketMinutes: number;
  overview: JevOverview;
  intent: IntentStatsReport;
  guardrail: GuardrailStats;
  relevance: RelevanceStats;
  routing: RoutingStats;
  domains?: DomainStats | null;
  /** Absent from an api older than the answer check. */
  answerCheck?: AnswerCheckStats | null;
}

/** One place in the repository matching a question (search_codebase, via /api/code/snippets). */
export interface CodeSnippet {
  path: string;
  startLine: number | null;
  endLine: number | null;
  symbol: string | null;
  section: string;
  kind: 'code' | 'docs';
  language: string;
  score: number;
  snippet: string;
}

export interface CodeSearchResult {
  results: CodeSnippet[];
  totalMatches: number;
  truncated: boolean;
  refineHint: string | null;
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
