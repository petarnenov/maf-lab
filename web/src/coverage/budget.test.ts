import { describe, expect, it } from 'vitest';
import { describeBudget, emptyBudget, parseBudget } from './budget';

const typed = (maxTokens: string, maxCostUsd: string) => ({
  maxTokens,
  maxCostUsd,
  tokensUnlimited: false,
  costUnlimited: false,
});

describe('budget', () => {
  it('reads empty fields as unlimited', () => {
    expect(parseBudget(typed('', ''))).toEqual({ budget: null, errors: {} });
    expect(parseBudget(emptyBudget)).toEqual({ budget: null, errors: {} });
    expect(describeBudget(null)).toBe('unlimited');
  });

  it('shows a cap below a cent as typed, not as $0.00', () => {
    expect(describeBudget({ maxTokens: null, maxCostUsd: 0.001 })).toBe('$0.001');
    expect(describeBudget({ maxTokens: 400000, maxCostUsd: 0.5 })).toBe('$0.50 or 400,000 tokens');
  });

  it('refuses a cap that is not positive, and tokens that are not whole', () => {
    expect(parseBudget(typed('0', '')).errors.maxTokens).toBeDefined();
    expect(parseBudget(typed('1.5', '')).errors.maxTokens).toBeDefined();
    expect(parseBudget(typed('', '-1')).errors.maxCostUsd).toBeDefined();
  });

  it('ignores what was typed in a cap left unlimited', () => {
    expect(parseBudget({ ...typed('0', '0.5'), tokensUnlimited: true })).toEqual({
      budget: { maxTokens: null, maxCostUsd: 0.5 },
      errors: {},
    });
  });
});
