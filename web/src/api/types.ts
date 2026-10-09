// Mirrors the DTOs in src/Maf.Lab.Domain (camelCase JSON). See docs/http-api.md.

/** The core's roles. Domain roles (billing's advisor, ops) are claims the core and this shell do not read. */
export type Role = 'TENANT_ADMIN' | 'USER' | 'READ_ONLY' | 'PLATFORM_ADMIN';

export interface SessionUser {
  userId: string;
  tenantId: string;
  role: Role;
  label: string;
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

// ---- Admin jobs ----

export type AdminJobState = 'queued' | 'running' | 'succeeded' | 'failed' | 'canceled';

/** A job in the core's admin job store: a plugin's long work (index runs) and coverage's refresh. */
export interface AdminJob {
  jobId: string;
  kind: string;
  state: AdminJobState;
  startedAt: string;
  finishedAt?: string | null;
  summary?: string | null;
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
