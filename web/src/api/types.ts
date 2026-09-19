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

export interface DoneData {
  conversationId: string;
  turnId: string;
  error?: string | null;
}

export type ChatStreamEvent =
  | { type: 'text_delta'; data: { text: string } }
  | { type: 'tool_call_started'; data: ToolCallStartedData }
  | { type: 'tool_call_finished'; data: ToolCallFinishedData }
  | { type: 'sources'; data: { sources: SourceRef[] } }
  | { type: 'trace'; data: TraceEvent }
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
}

export interface ChatRequest {
  conversationId?: string;
  message: string;
}

// ---- Feedback ----

export type FeedbackKind = 'wrong_tool' | 'wrong_document' | 'wrong_answer';

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

export interface EvalReportSummary {
  runId: string;
  suite: string;
  startedAt: string;
  passed: boolean;
  variants: EvalVariantResult[];
}

export interface EvalReport {
  runId: string;
  suite: string;
  startedAt: string;
  finishedAt: string;
  settings: Record<string, string>;
  variants: EvalVariantResult[];
  passed: boolean;
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
