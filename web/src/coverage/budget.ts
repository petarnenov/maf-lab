import type { RunBudget } from '../api/types';

/** The budget fields as typed: empty means that cap is unlimited. */
export interface BudgetInput {
  maxTokens: string;
  maxCostUsd: string;
}

export const emptyBudget: BudgetInput = { maxTokens: '', maxCostUsd: '' };

export interface ParsedBudget {
  /** What the start request carries; null when both caps are left empty (unlimited). */
  budget: RunBudget | null;
  errors: { maxTokens?: string; maxCostUsd?: string };
}

/** Reads the fields: a cap that is set must be positive, and max tokens a whole number. */
export function parseBudget(input: BudgetInput): ParsedBudget {
  const errors: ParsedBudget['errors'] = {};
  const tokensText = input.maxTokens.trim();
  const costText = input.maxCostUsd.trim();
  const tokens = tokensText === '' ? null : Number(tokensText);
  const cost = costText === '' ? null : Number(costText);
  if (tokens !== null && !(Number.isInteger(tokens) && tokens > 0))
    errors.maxTokens = 'Enter a whole number above 0, or leave it empty.';
  if (cost !== null && !(Number.isFinite(cost) && cost > 0))
    errors.maxCostUsd = 'Enter an amount above 0, or leave it empty.';
  const budget = tokens === null && cost === null ? null : { maxTokens: tokens, maxCostUsd: cost };
  return { budget, errors };
}

/** Whether a run's budget sets no cap at all. */
export function isUnlimited(budget: RunBudget | null | undefined): boolean {
  return budget?.maxTokens == null && budget?.maxCostUsd == null;
}

/** An amount as typed, so a cap below a cent is not shown as $0.00: two decimals, or as many as it needs. */
const dollars = (value: number) =>
  `$${value >= 0.01 ? value.toFixed(2) : value.toLocaleString('en-US', { maximumSignificantDigits: 3 })}`;

/** A budget in a few words: "unlimited", "$0.50", "400,000 tokens" or both. */
export function describeBudget(budget: RunBudget | null | undefined): string {
  if (isUnlimited(budget)) return 'unlimited';
  const parts: string[] = [];
  if (budget?.maxCostUsd != null) parts.push(dollars(budget.maxCostUsd));
  if (budget?.maxTokens != null) parts.push(`${budget.maxTokens.toLocaleString('en-US')} tokens`);
  return parts.join(' or ');
}
