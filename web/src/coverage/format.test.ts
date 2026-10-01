import { describe, expect, it } from 'vitest';
import { elapsed } from './format';

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
