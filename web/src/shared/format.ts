export function formatMetric(value: number): string {
  if (!Number.isFinite(value)) return String(value);
  if (Number.isInteger(value)) return String(value);
  return value.toFixed(3);
}

export function formatDate(iso: string): string {
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? iso : date.toLocaleString();
}

/**
 * How a data card (or any page answering a question) writes its numbers: in the language of the question it answers (Bulgarian when the question is
 * written in Cyrillic, as the server's refusals decide it), in the card's own currency, whole units for money and one
 * decimal for weights.
 */
export type Lang = 'bg' | 'en';

export const langOf = (question: string): Lang => (/[Ѐ-ӿ]/.test(question) ? 'bg' : 'en');

const locale = (lang: Lang) => (lang === 'bg' ? 'bg-BG' : 'en-US');

export function money(value: number, currency: string, lang: Lang, signed = false): string {
  return new Intl.NumberFormat(locale(lang), {
    style: 'currency',
    currency,
    currencyDisplay: 'narrowSymbol',
    maximumFractionDigits: 0,
    signDisplay: signed ? 'exceptZero' : 'auto',
  }).format(value);
}

/** A weight given in percent (20.6 means 20.6 %). Bulgarian puts a space before the sign, English does not. */
export function percent(value: number, lang: Lang, signed = false): string {
  const number = new Intl.NumberFormat(locale(lang), {
    minimumFractionDigits: 1,
    maximumFractionDigits: 1,
    signDisplay: signed ? 'exceptZero' : 'auto',
  }).format(value);
  return lang === 'bg' ? `${number}\u00a0%` : `${number}%`;
}

export function date(iso: string, lang: Lang): string {
  const d = new Date(`${iso}T00:00:00Z`);
  return Number.isNaN(d.getTime())
    ? iso
    : new Intl.DateTimeFormat(locale(lang), { dateStyle: 'medium', timeZone: 'UTC' }).format(d);
}

/** Rows as CSV: plain numbers, quoted text, a header first. */
export function toCsv(
  header: string[],
  rows: (string | number | boolean | null | undefined)[][],
): string {
  const cell = (v: string | number | boolean | null | undefined) =>
    typeof v === 'number' || typeof v === 'boolean'
      ? String(v)
      : v == null
        ? ''
        : /[",\n]/.test(v)
          ? `"${v.replace(/"/g, '""')}"`
          : v;
  return [header, ...rows].map((r) => r.map(cell).join(',')).join('\n');
}
