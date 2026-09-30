import { describe, expect, it } from 'vitest';
import { describeBudget, parseBudget } from './budget';

describe('budget', () => {
  it('reads empty fields as unlimited', () => {
    expect(parseBudget({ maxTokens: '', maxCostUsd: '' })).toEqual({ budget: null, errors: {} });
    expect(describeBudget(null)).toBe('unlimited');
  });

  it('shows a cap below a cent as typed, not as $0.00', () => {
    expect(describeBudget({ maxTokens: null, maxCostUsd: 0.001 })).toBe('$0.001');
    expect(describeBudget({ maxTokens: 400000, maxCostUsd: 0.5 })).toBe('$0.50 or 400,000 tokens');
  });

  it('refuses a cap that is not positive, and tokens that are not whole', () => {
    expect(parseBudget({ maxTokens: '0', maxCostUsd: '' }).errors.maxTokens).toBeDefined();
    expect(parseBudget({ maxTokens: '1.5', maxCostUsd: '' }).errors.maxTokens).toBeDefined();
    expect(parseBudget({ maxTokens: '', maxCostUsd: '-1' }).errors.maxCostUsd).toBeDefined();
  });
});
