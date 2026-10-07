// The shapes of the statistics routes (this plugin's docs/http-api.md), and the part of the evals screen's report
// summary the intent eval history reads.

/** One failed case of an eval run. */
export interface EvalCaseFailure {
  caseId: string;
  reason: string;
}

/** One variant of an eval run. */
export interface EvalVariantResult {
  name: string;
  metrics: Record<string, number>;
  thresholds: Record<string, number>;
  passed: boolean;
  cases: number;
  failures: EvalCaseFailure[];
}

/** What the intent eval history reads of an eval report summary (`GET /api/evals/reports`). */
export interface EvalReportSummary {
  runId: string;
  suite: string;
  startedAt: string;
  passed: boolean;
  variants: EvalVariantResult[];
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
