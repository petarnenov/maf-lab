import type { TraceEvent } from '../api/types';

// Typed views over TraceEvent.data per kind (docs/trace-events.md). Data can be truncated to
// { truncated: true }, so every accessor tolerates missing fields.

export interface Candidate {
  rank: number;
  chunkId: string;
  docId: string;
  tenantId: string;
  sectionPath: string;
  score: number;
}

export interface MessageContent {
  type: 'text' | 'functionCall' | 'functionResult' | string;
  text?: string;
  callId?: string;
  name?: string;
  arguments?: unknown;
  result?: unknown;
}

export interface TraceMessage {
  role: string;
  contents: MessageContent[];
}

export interface TurnStartData {
  conversationId?: string;
  turnId?: string;
  principal?: { userId?: string; firmId?: string; role?: string };
  apiInstance?: string;
  question?: string;
}

export interface IntentData {
  intent?: string;
  forcedRetrieval?: boolean;
  forcedTool?: string | null;
}

export interface HistoryData {
  budgetTokens?: number;
  usedTokens?: number;
  included?: { role: string; text: string; tokens: number }[];
  excludedCount?: number;
}

export interface PromptData {
  version?: string;
  systemPrompt?: string;
  toolMode?: string;
  tools?: { name: string; description?: string; inputSchema?: unknown }[];
}

export interface ModelRequestData {
  iteration?: number;
  model?: string;
  endpoint?: string;
  toolMode?: string;
  temperature?: number | null;
  think?: boolean | null;
  tools?: string[];
  messages?: TraceMessage[];
}

export interface ModelResponseData {
  iteration?: number;
  model?: string;
  text?: string;
  toolCalls?: { callId: string; name: string; arguments?: unknown }[];
  finishReason?: string | null;
  usage?: {
    inputTokens?: number | null;
    outputTokens?: number | null;
    totalTokens?: number | null;
  } | null;
  latencyMs?: number;
}

export interface ToolCallData {
  callId?: string;
  tool?: string;
  arguments?: unknown;
  reason?: string;
}

export interface ToolResultData {
  callId?: string;
  tool?: string;
  isError?: boolean;
  latencyMs?: number;
  mcpInstance?: string | null;
  result?: unknown;
}

export interface RetrievalData {
  callId?: string;
  instance?: string;
  tenantScope?: string[];
  settings?: {
    mode?: string;
    fusion?: string;
    denseVector?: string;
    limit?: number;
    prefetchLimit?: number;
    rerank?: boolean;
  };
  query?: {
    text?: string;
    terms?: { term: string; idf: number }[];
    denseModel?: string;
    denseDims?: number;
  };
  dense?: Candidate[];
  sparse?: Candidate[];
  fused?: Candidate[];
  rerank?: string[] | null;
  timings?: { embedMs?: number; sparseEncodeMs?: number; qdrantMs?: number; rerankMs?: number };
}

export interface EnvelopeData {
  callId?: string;
  tool?: string;
  text?: string;
}

export interface AuditData {
  tool?: string;
  arguments?: string;
  outcome?: string;
  durationMs?: number;
}

export interface TurnEndData {
  durationMs?: number;
  error?: string | null;
  answerChars?: number;
  toolCalls?: number;
  sourceCount?: number;
}

/** Data of an event, typed by the caller; always an object (never null). */
export function dataOf<T>(event: TraceEvent | undefined): T {
  const data = event?.data;
  return (data && typeof data === 'object' ? data : {}) as T;
}

export const byKind = (events: TraceEvent[], kind: string) => events.filter((e) => e.kind === kind);

export const firstOf = (events: TraceEvent[], kind: string) => events.find((e) => e.kind === kind);

/** Total turn duration: turn.end durationMs, otherwise the latest end time seen so far. */
export function totalDuration(events: TraceEvent[]): number {
  const end = dataOf<TurnEndData>(firstOf(events, 'turn.end')).durationMs;
  if (typeof end === 'number') return end;
  return events.reduce((max, e) => Math.max(max, e.atMs + (e.durationMs ?? 0)), 0);
}

/** Distinct MCP replicas that served this turn's tool calls. */
export function mcpInstances(events: TraceEvent[]): string[] {
  const names = new Set<string>();
  for (const e of byKind(events, 'tool.result')) {
    const name = dataOf<ToolResultData>(e).mcpInstance;
    if (name) names.add(name);
  }
  for (const e of byKind(events, 'retrieval')) {
    const name = dataOf<RetrievalData>(e).instance;
    if (name) names.add(name);
  }
  return [...names];
}

export function formatMs(ms: number | null | undefined): string {
  if (ms === null || ms === undefined) return 'n/a';
  return ms >= 1000 ? `${(ms / 1000).toFixed(2)} s` : `${Math.round(ms)} ms`;
}
