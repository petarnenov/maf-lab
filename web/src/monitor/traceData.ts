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
  /** True for a candidate the relevance floor kept out of the answer. Diagnostics list it anyway. */
  belowFloor?: boolean;
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
  /** The trace this turn's spans are in, and where to open it. Absent without a trace store. */
  traceId?: string;
  traceUrl?: string;
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
    reranker?: string | null;
    relevanceGate?: boolean;
    /** Lowest score a candidate may have on each branch and still count. Null means that branch has no floor. */
    denseFloor?: number | null;
    sparseFloor?: number | null;
  };
  query?: {
    /** The text actually embedded and encoded. */
    text?: string;
    /** What the user asked, when it was translated into the corpus language before searching. */
    original?: string;
    translated?: boolean;
    translationMs?: number | null;
    translationNote?: string | null;
    /** A term the indexed corpus has never seen has no IDF weight and cannot match on BM25. */
    terms?: { term: string; idf: number | null; inVocabulary: boolean }[];
    denseModel?: string;
    denseDims?: number;
  };
  dense?: Candidate[];
  sparse?: Candidate[];
  fused?: Candidate[];
  rerank?: string[] | null;
  /** Jev's judgment of the fused candidates; null when no judge ran. Carries the probability of each chunk. */
  relevance?: (RelevanceData & { scores?: { chunkId: string; p: number }[] | null }) | null;
  timings?: {
    embedMs?: number;
    sparseEncodeMs?: number;
    qdrantMs?: number;
    rerankMs?: number;
    relevanceMs?: number;
  };
}

/** One Neo4j read a graph tool call made: the template, what came back and how it went. Structure only. */
export interface GraphReadData {
  query?: string;
  limit?: number;
  rows?: number;
  truncated?: boolean;
  durationMs?: number;
  /** ok, unavailable (the graph store could not be reached), cancelled or error. */
  outcome?: string;
  /** The exception's type name for a read that did not succeed; never its message. */
  errorType?: string | null;
}

/**
 * A graph tool call's reads of Neo4j (the `graph` event, add-graph-trace-event). Never an argument value: those are
 * in the same call's `tool.call`.
 */
export interface GraphData {
  callId?: string;
  tool?: string;
  instance?: string | null;
  tenantScope?: string[];
  reads?: GraphReadData[];
  rows?: number;
  truncated?: boolean;
  /** Total time spent in Neo4j. */
  durationMs?: number;
  outcome?: string;
}

/**
 * Jev's relevance judgment of one search (the `relevance` event, and the shared part of `retrieval.relevance`).
 * Numbers only — no query, passage or chunk id.
 */
export interface RelevanceData {
  callId?: string;
  gate?: boolean;
  /** The reranker in use (`jev`, `llm`), null when rerank is off. */
  reranker?: string | null;
  floor?: number;
  judged?: number;
  /** Null when Jev did not answer. */
  max?: number | null;
  silenced?: boolean;
  /** Jev's answer actually ordered the results. */
  rerankedByJev?: boolean;
  model?: string | null;
  durationMs?: number;
  /** Why Jev did not answer; the search was then left ungated. */
  reason?: string | null;
}

/**
 * Jev's check of the final answer (the `answer.check` event). Numbers only — never the answer or the sources' text.
 */
export interface AnswerCheckData {
  /**
   * pass, uncertain (in the review band: no signal, so no header chip), not_relevant, not_grounded or unchecked. A
   * verdict this page does not know is read as neutral too.
   */
  verdict?: string;
  relevant?: number | null;
  grounded?: number | null;
  /** The signal floors: below them the turn is flagged. */
  relevantFloor?: number;
  groundedFloor?: number;
  /** The pass thresholds: at or above both the answer passes; in between it is uncertain. */
  relevantPassAt?: number;
  groundedPassAt?: number;
  /** billing or codebase: the context the two questions were asked in. */
  context?: string;
  /** Sources dropped because the same source came first. */
  duplicates?: number;
  model?: string | null;
  durationMs?: number;
  /** Why there is no verdict (disabled, no key, timed out, rejected). */
  reason?: string | null;
  sources?: number;
  /** How many of the previous turn's sources were sent beside this turn's, for a follow-up. */
  previousSources?: number;
  sourceChars?: number;
  requests?: number;
}

/**
 * "not grounded" / "not relevant" when Jev's answer check flagged the turn's answer; null otherwise — a pass, an
 * uncertain answer (the review band raises no signal) and an unchecked one are all neutral.
 */
export function failedAnswerCheck(events: TraceEvent[]): string | null {
  const verdict = dataOf<AnswerCheckData>(byKind(events, 'answer.check').at(-1)).verdict;
  return verdict === 'not_grounded' || verdict === 'not_relevant'
    ? verdict.replace('_', ' ')
    : null;
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
  for (const e of byKind(events, 'graph')) {
    const name = dataOf<GraphData>(e).instance;
    if (name) names.add(name);
  }
  return [...names];
}

/** Neo4j reads made by the turn's graph tool calls so far (an event missing its reads counts as none). */
export function graphReadCount(events: TraceEvent[]): number {
  return byKind(events, 'graph').reduce((n, e) => {
    const reads = dataOf<GraphData>(e).reads;
    return n + (Array.isArray(reads) ? reads.length : 0);
  }, 0);
}

export function formatMs(ms: number | null | undefined): string {
  if (ms === null || ms === undefined) return 'n/a';
  return ms >= 1000 ? `${(ms / 1000).toFixed(2)} s` : `${Math.round(ms)} ms`;
}
