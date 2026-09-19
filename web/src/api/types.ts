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
  | { type: 'done'; data: DoneData };

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
