/**
 * How a data card writes its numbers: in the language of the question it answers (Bulgarian when the question is
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

/** The words a card is labelled with, in each language. */
export const words = {
  bg: {
    assetClass: 'Клас активи',
    value: 'Стойност',
    target: 'Цел',
    actual: 'Текущо',
    drift: 'Отклонение',
    trade: 'Сделка',
    after: 'След',
    total: 'Общо',
    buy: 'Покупка',
    sell: 'Продажба',
    none: '—',
    outside: 'извън толеранса',
    rebalanceNeeded: 'Нужно е ребалансиране',
    rebalanceNotNeeded: 'Не е нужно ребалансиране',
    tolerance: 'толеранс',
    asOf: 'към',
    copy: 'Копирай като CSV',
    copied: 'Копирано',
    quarterEnd: 'Край на тримесечие',
    aum: 'AUM',
    change: 'Промяна',
    aumOf: 'AUM по тримесечия',
    account: 'Сметка',
    name: 'Име',
    household: 'Домакинство',
    model: 'Модел',
    currency: 'Валута',
    myAccounts: 'Моите сметки',
  },
  en: {
    assetClass: 'Asset class',
    value: 'Value',
    target: 'Target',
    actual: 'Actual',
    drift: 'Drift',
    trade: 'Trade',
    after: 'After',
    total: 'Total',
    buy: 'Buy',
    sell: 'Sell',
    none: '—',
    outside: 'outside tolerance',
    rebalanceNeeded: 'Rebalance needed',
    rebalanceNotNeeded: 'No rebalance needed',
    tolerance: 'tolerance',
    asOf: 'as of',
    copy: 'Copy as CSV',
    copied: 'Copied',
    quarterEnd: 'Quarter end',
    aum: 'AUM',
    change: 'Change',
    aumOf: 'Quarter-end AUM',
    account: 'Account',
    name: 'Name',
    household: 'Household',
    model: 'Model',
    currency: 'Currency',
    myAccounts: 'My accounts',
  },
} as const;
