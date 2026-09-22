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

export type ChatStreamEvent =
  | { type: 'text_delta'; data: { text: string } }
  | { type: 'tool_call_started'; data: ToolCallStartedData }
  | { type: 'tool_call_finished'; data: ToolCallFinishedData }
  | { type: 'sources'; data: { sources: SourceRef[] } }
  | { type: 'reasoning_delta'; data: { text: string } }
  /** The model stopped reasoning. It does not close the block — the answer's first text does. */
  | { type: 'reasoning_end' }
  | { type: 'trace'; data: TraceEvent }
  | { type: 'agui_frame'; data: AguiFrame }
  | { type: 'confirmation_required'; data: ConfirmationRequiredData }
  | { type: 'done'; data: DoneData };

// ---- Turn trace (behind the scenes). See docs/trace-events.md. ----

export type TraceKind =
  | 'turn.start'
  | 'intent'
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
  | 'sources'
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
  | 'long_answer_without_sources';

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
}

export interface ConversationDetail {
  conversationId: string;
  title: string;
  createdAt: string;
  lastActivityAt: string;
  turns: HistoryTurn[];
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
  latencyMs: number | null;
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
