/** Maps a value in [d0, d1] onto [r0, r1]. A flat domain maps everything to r0 rather than dividing by zero. */
export function linear(d0: number, d1: number, r0: number, r1: number) {
  const span = d1 - d0;
  return (value: number) => (span === 0 ? r0 : r0 + ((value - d0) / span) * (r1 - r0));
}

/**
 * The smallest "clean" axis maximum at or above a value: 1, 2, 5 or 10 times a power of ten. Counts of turns are
 * whole, so the least a count axis goes to is 1.
 */
export function niceMax(value: number): number {
  if (!Number.isFinite(value) || value <= 1) return 1;
  const power = 10 ** Math.floor(Math.log10(value));
  for (const step of [1, 2, 5, 10]) {
    if (step * power >= value) return step * power;
  }
  return 10 * power;
}

/** Ticks from 0 to a nice maximum on whole, round steps: counts of turns never read 2.5. */
export function ticks(max: number): number[] {
  const top = niceMax(max);
  const lead = top / 10 ** Math.floor(Math.log10(top));
  const step = top <= 5 ? 1 : lead === 2 ? top / 4 : top / 5;
  const out: number[] = [];
  for (let v = 0; v <= top + 1e-9; v += step) out.push(Number(v.toFixed(6)));
  return out;
}

export function percent(part: number, whole: number): string {
  if (whole === 0) return '–';
  const value = (part / whole) * 100;
  return `${value >= 10 || value === 0 ? Math.round(value) : value.toFixed(1)}%`;
}

export function formatMs(ms: number | null | undefined): string {
  if (ms == null) return '–';
  if (ms >= 10_000) return `${(ms / 1000).toFixed(0)} s`;
  if (ms >= 1000) return `${(ms / 1000).toFixed(2)} s`;
  return `${Math.round(ms)} ms`;
}

/** A bucket's start as a short axis label: a time for windows up to a day, a day and hour beyond. */
export function bucketLabel(iso: string, bucketMinutes: number): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;
  const time = date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  if (bucketMinutes < 360) return time;
  return `${date.toLocaleDateString([], { month: 'short', day: 'numeric' })} ${time}`;
}
