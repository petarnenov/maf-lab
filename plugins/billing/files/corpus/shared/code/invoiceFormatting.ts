// Invoice formatting helpers used by the invoice renderer and client portal.
// Pure functions: no I/O, no locale guessing (reporting currency is explicit).

export interface InvoiceLine {
  accountNumber: string;
  scheduleCode: string;
  billableAum: number;
  annualRate: number; // e.g. 0.0075 for 0.75%
  billableDays: number;
  fee: number;
  kind: 'fee' | 'adjustment' | 'credit';
}

export interface Invoice {
  invoiceNumber: string;
  periodStart: string; // yyyy-MM-dd
  periodEnd: string;
  method: 'arrears' | 'advance';
  lines: InvoiceLine[];
}

/** Formats an amount with two decimals and thousands separators. */
export function formatMoney(amount: number, currency = 'USD'): string {
  return new Intl.NumberFormat('en-US', { style: 'currency', currency, minimumFractionDigits: 2 }).format(amount);
}

/** Formats an annual rate as a percentage with up to four decimals. */
export function formatRate(rate: number): string {
  return `${(rate * 100).toFixed(4).replace(/0+$/, '').replace(/\.$/, '')}%`;
}

/** Builds an invoice number like INV-2026-000123 from the period end year and sequence. */
export function invoiceNumber(periodEnd: string, sequence: number): string {
  return `INV-${periodEnd.slice(0, 4)}-${String(sequence).padStart(6, '0')}`;
}

/** Invoice total; credits reduce it but the total never goes below zero. */
export function invoiceTotal(invoice: Invoice): { total: number; carryForward: number } {
  const raw = invoice.lines.reduce((sum, l) => sum + (l.kind === 'credit' ? -Math.abs(l.fee) : l.fee), 0);
  const rounded = Math.round(raw * 100) / 100;
  return rounded < 0 ? { total: 0, carryForward: -rounded } : { total: rounded, carryForward: 0 };
}

/** Renders one line of the plain-text invoice summary. */
export function renderLine(line: InvoiceLine): string {
  return [
    line.accountNumber.padEnd(12),
    line.scheduleCode.padEnd(16),
    formatMoney(line.billableAum).padStart(16),
    formatRate(line.annualRate).padStart(9),
    `${line.billableDays}d`.padStart(5),
    formatMoney(line.fee).padStart(14),
  ].join(' ');
}
