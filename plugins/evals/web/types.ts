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
