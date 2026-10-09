import { describe, expect, it } from 'vitest';
import { cost, elapsed } from './format';

describe('elapsed', () => {
  it('reads seconds under a minute', () => {
    expect(elapsed(0)).toBe('0s');
    expect(elapsed(42_000)).toBe('42s');
    expect(elapsed(59_999)).toBe('59s');
  });

  it('reads minutes and two-digit seconds under an hour', () => {
    expect(elapsed(60_000)).toBe('1m 00s');
    expect(elapsed(185_000)).toBe('3m 05s');
    expect(elapsed(3_599_000)).toBe('59m 59s');
  });

  it('reads hours and two-digit minutes beyond', () => {
    expect(elapsed(3_600_000)).toBe('1h 00m');
    expect(elapsed(3_725_000)).toBe('1h 02m');
    expect(elapsed(26 * 3_600_000 + 5 * 60_000)).toBe('26h 05m');
  });

  it('reads a dash when there is no duration, and never a negative one', () => {
    expect(elapsed(null)).toBe('—');
    expect(elapsed(undefined)).toBe('—');
    expect(elapsed(Number.NaN)).toBe('—');
    expect(elapsed(-5_000)).toBe('0s');
  });
});

describe('cost', () => {
  it('reads three decimals under a dollar and two from one', () => {
    expect(cost(0.0421)).toBe('$0.042');
    expect(cost(0.291)).toBe('$0.291');
    expect(cost(0.001)).toBe('$0.001');
    expect(cost(1.2745)).toBe('$1.27');
    expect(cost(0.9996)).toBe('$1.00');
    expect(cost(1234.5)).toBe('$1,234.50');
  });

  it('reads an amount too small for three decimals as under a tenth of a cent', () => {
    expect(cost(0.0004)).toBe('<$0.001');
    expect(cost(0.0001)).toBe('<$0.001');
  });

  it('reads nothing spent as $0.00 and an unknown cost as a dash', () => {
    expect(cost(0)).toBe('$0.00');
    expect(cost(0, true)).toBe('$0.00');
    expect(cost(null)).toBe('—');
    expect(cost(undefined)).toBe('—');
    expect(cost(Number.NaN)).toBe('—');
  });

  it('marks an amount priced at estimated rates', () => {
    expect(cost(0.0421, true)).toBe('≈$0.042');
    expect(cost(1.2745, true)).toBe('≈$1.27');
    expect(cost(0.0004, true)).toBe('≈<$0.001');
  });
});
