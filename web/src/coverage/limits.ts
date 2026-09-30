import type { AgentModel, EstimateParts, LimitBounds, RunLimits, RunLimitsInput } from '../api/types';

/** The limits a run may lower, as typed. */
export interface LimitsInput {
  maxAttempts: string;
  toolRoundsPerAttempt: string;
  testRunsPerAttempt: string;
  deadlineMinutes: string;
  maxSuspectedBugs: string;
}

export type LimitKey = keyof LimitsInput;

export const LIMIT_KEYS: LimitKey[] = [
  'maxAttempts',
  'toolRoundsPerAttempt',
  'testRunsPerAttempt',
  'deadlineMinutes',
  'maxSuspectedBugs',
];

/** The fields filled with the server's defaults. */
export const defaultLimits = (limits: RunLimits): LimitsInput =>
  Object.fromEntries(LIMIT_KEYS.map((key) => [key, String(limits[key].default)])) as unknown as LimitsInput;

export interface ParsedLimits {
  /** Every limit as a number, or null while any is invalid. */
  limits: Required<RunLimitsInput> | null;
  errors: Partial<Record<LimitKey, string>>;
}

/** Reads the fields: each must be a whole number within its bounds. */
export function parseLimits(input: LimitsInput, bounds: RunLimits): ParsedLimits {
  const errors: ParsedLimits['errors'] = {};
  const values = {} as Required<RunLimitsInput>;
  for (const key of LIMIT_KEYS) {
    const b: LimitBounds = bounds[key];
    const text = input[key].trim();
    const value = Number(text);
    if (text === '' || !Number.isInteger(value) || value < b.min || value > b.max)
      errors[key] = `Enter a whole number from ${b.min} to ${b.max}.`;
    else values[key] = value;
  }
  return { limits: Object.keys(errors).length === 0 ? values : null, errors };
}

export interface RunEstimate {
  inputTokens: number;
  outputTokens: number;
  tokens: number;
  costUsd: number;
  perAttemptUsd: number;
}

/** Dollars as the server rounds them: to four decimals. */
const round4 = (value: number) => Math.round(value * 10_000) / 10_000;

/**
 * What a run is expected to use for these limits, at this model's prices: the same formula the api and the agent use
 * (AttemptEstimate), so both sides give the same figure.
 */
export function estimateRun(
  parts: EstimateParts,
  model: Pick<AgentModel, 'inputPerMTok' | 'outputPerMTok'>,
  attempts: number,
  toolRounds: number,
): RunEstimate {
  const perAttemptInput = parts.fixedInputTokens + parts.inputTokensPerRound * Math.min(toolRounds, parts.typicalRounds);
  const price = (input: number, output: number) =>
    round4((input / 1_000_000) * model.inputPerMTok + (output / 1_000_000) * model.outputPerMTok);
  const inputTokens = perAttemptInput * attempts;
  const outputTokens = parts.outputTokensPerAttempt * attempts;
  return {
    inputTokens,
    outputTokens,
    tokens: inputTokens + outputTokens,
    costUsd: price(inputTokens, outputTokens),
    perAttemptUsd: price(perAttemptInput, parts.outputTokensPerAttempt),
  };
}
