import { describe, expect, it } from 'vitest';
import { formatMs, linear, niceMax, percent, ticks } from './scale';

describe('chart scales', () => {
  it('maps a domain onto a range, and a flat domain onto its start', () => {
    const y = linear(0, 10, 100, 0);
    expect(y(0)).toBe(100);
    expect(y(5)).toBe(50);
    expect(y(10)).toBe(0);
    expect(linear(3, 3, 7, 9)(3)).toBe(7);
  });

  it('rounds an axis up to 1, 2 or 5 times a power of ten, never below one turn', () => {
    expect(niceMax(0)).toBe(1);
    expect(niceMax(3)).toBe(5);
    expect(niceMax(7)).toBe(10);
    expect(niceMax(11)).toBe(20);
    expect(niceMax(430)).toBe(500);
  });

  it('ticks whole numbers on a small axis', () => {
    expect(ticks(3)).toEqual([0, 1, 2, 3, 4, 5]);
    expect(ticks(18)).toEqual([0, 5, 10, 15, 20]);
  });

  it('formats shares and latencies for people', () => {
    expect(percent(1, 3)).toBe('33%');
    expect(percent(1, 200)).toBe('0.5%');
    expect(percent(0, 0)).toBe('–');
    expect(formatMs(284.4)).toBe('284 ms');
    expect(formatMs(2003)).toBe('2.00 s');
    expect(formatMs(36_000)).toBe('36 s');
    expect(formatMs(null)).toBe('–');
  });
});
