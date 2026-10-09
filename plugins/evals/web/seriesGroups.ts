/**
 * Groups eval series ids (`suite/variant/metric`, with an optional `metric:part` breakdown) for the trend picker.
 * Everything is derived from the ids themselves: the lists below only say which known suites and variants lead, and
 * anything they do not name still gets its own group, after the named ones.
 */

export type SeriesOption = { value: string; label: string };
export type SeriesGroup = { key: string; label: string; options: SeriesOption[] };

/** Pipeline order: what the agent picks, what it finds, what it says, how it holds up, then the side suites. */
const SUITE_ORDER = [
  'selection',
  'retrieval',
  'generation',
  'injection',
  'confirmation',
  'intent',
  'a2a-conformance',
];

/** Production variant first. */
const VARIANT_ORDER: Record<string, string[]> = {
  retrieval: ['hybrid', 'hybrid-dbsf', 'dense', 'sparse'],
};

/** The scripts questions arrive in, in the order the retrieval spec names them. */
const LANGUAGE_ORDER = ['en', 'bg', 'bg-latn'];
const LANGUAGE_CODE = /^[a-z]{2}(-[a-z0-9]+)*$/i;

type Parsed = {
  value: string;
  suite: string;
  variant: string;
  base: string;
  part: string | null;
};

export function parseSeries(value: string): Parsed {
  const [suite, variant = '', ...rest] = value.split('/');
  const metric = rest.join('/');
  const colon = metric.indexOf(':');
  return colon < 0
    ? { value, suite, variant, base: metric, part: null }
    : { value, suite, variant, base: metric.slice(0, colon), part: metric.slice(colon + 1) };
}

export function groupLabel(value: string): string {
  const { suite, variant } = parseSeries(value);
  return variant ? `${suite} · ${variant}` : suite;
}

export function optionLabel(value: string): string {
  const { base, part } = parseSeries(value);
  return part === null ? base : `${base} · ${part}`;
}

/** Listed names first in list order, then the rest alphabetically. */
function byPreference(order: string[]) {
  return (a: string, b: string) => {
    const ia = order.indexOf(a);
    const ib = order.indexOf(b);
    if (ia >= 0 || ib >= 0) {
      return (ia < 0 ? Infinity : ia) - (ib < 0 ? Infinity : ib);
    }
    return a.localeCompare(b);
  };
}

function family(base: string): { name: string; k: number } {
  const at = base.indexOf('@');
  if (at < 0) return { name: base, k: -1 };
  const k = Number(base.slice(at + 1));
  return { name: base.slice(0, at), k: Number.isFinite(k) ? k : -1 };
}

/**
 * A metric that is broken down is the one worth breaking down, so it leads; the rest of its family
 * (`recall@20` after `recall@5`) follows; the remaining metrics are alphabetical.
 */
function orderBases(bases: string[], brokenDown: Set<string>): string[] {
  const leadFamilies = new Set([...brokenDown].map((b) => family(b).name));
  const rank = (base: string) =>
    brokenDown.has(base) ? 0 : leadFamilies.has(family(base).name) ? 1 : 2;
  return [...bases].sort((a, b) => {
    const r = rank(a) - rank(b);
    if (r !== 0) return r;
    const fa = family(a);
    const fb = family(b);
    if (fa.name === fb.name && fa.k !== fb.k) return fa.k - fb.k;
    return a.localeCompare(b);
  });
}

/** Languages first (known scripts, then any other language code), then other splits. */
function comparePart(a: string, b: string): number {
  const rank = (part: string) =>
    LANGUAGE_ORDER.includes(part) ? 0 : LANGUAGE_CODE.test(part) ? 1 : 2;
  const r = rank(a) - rank(b);
  if (r !== 0) return r;
  if (rank(a) === 0) return LANGUAGE_ORDER.indexOf(a) - LANGUAGE_ORDER.indexOf(b);
  return a.localeCompare(b);
}

export function groupSeries(values: string[]): SeriesGroup[] {
  const bySuite = new Map<string, Map<string, Parsed[]>>();
  for (const value of new Set(values)) {
    const parsed = parseSeries(value);
    const variants = bySuite.get(parsed.suite) ?? new Map<string, Parsed[]>();
    variants.set(parsed.variant, [...(variants.get(parsed.variant) ?? []), parsed]);
    bySuite.set(parsed.suite, variants);
  }

  const groups: SeriesGroup[] = [];
  for (const suite of [...bySuite.keys()].sort(byPreference(SUITE_ORDER))) {
    const variants = bySuite.get(suite)!;
    for (const variant of [...variants.keys()].sort(byPreference(VARIANT_ORDER[suite] ?? []))) {
      const series = variants.get(variant)!;
      const brokenDown = new Set(series.filter((s) => s.part !== null).map((s) => s.base));
      const bases = orderBases([...new Set(series.map((s) => s.base))], brokenDown);
      const options: SeriesOption[] = [];
      for (const base of bases) {
        const ofBase = series.filter((s) => s.base === base);
        // A breakdown whose whole-metric series is missing still sits where that metric would.
        ofBase.sort((a, b) =>
          a.part === null ? -1 : b.part === null ? 1 : comparePart(a.part, b.part),
        );
        options.push(...ofBase.map((s) => ({ value: s.value, label: optionLabel(s.value) })));
      }
      const key = `${suite}/${variant}`;
      groups.push({ key, label: groupLabel(series[0].value), options });
    }
  }
  return groups;
}
