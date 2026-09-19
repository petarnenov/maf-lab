// Matches custodian fee-deduction confirmations to invoice lines and classifies exceptions.

export interface BilledFee { accountNumber: string; invoiceNumber: string; amount: number; }
export interface Deduction { accountNumber: string; amount: number; status: 'ok' | 'insufficient-cash' | 'account-closed' | 'not-found'; }

export type ExceptionKind = 'insufficient-cash' | 'account-closed' | 'not-found' | 'amount-difference' | 'missing-confirmation';

export interface ReconciliationException { accountNumber: string; invoiceNumber?: string; kind: ExceptionKind; difference: number; }

/** Default tolerance for custodian rounding differences, in currency units. */
export const ROUNDING_TOLERANCE = 0.01;

/** Indexes deductions by account number (one deduction per account per file). */
export function indexDeductions(deductions: Deduction[]): Map<string, Deduction> {
  return new Map(deductions.map((d) => [d.accountNumber, d]));
}

/** Classifies a single billed fee against its confirmation, or returns null when it reconciles. */
export function classify(fee: BilledFee, deduction: Deduction | undefined, tolerance = ROUNDING_TOLERANCE): ReconciliationException | null {
  if (!deduction) return { accountNumber: fee.accountNumber, invoiceNumber: fee.invoiceNumber, kind: 'missing-confirmation', difference: fee.amount };
  if (deduction.status !== 'ok') return { accountNumber: fee.accountNumber, invoiceNumber: fee.invoiceNumber, kind: deduction.status, difference: fee.amount };
  const diff = Math.round((fee.amount - deduction.amount) * 100) / 100;
  return Math.abs(diff) <= tolerance ? null : { accountNumber: fee.accountNumber, invoiceNumber: fee.invoiceNumber, kind: 'amount-difference', difference: diff };
}

/** Reconciles a whole fee file; the period can close only when the result is empty. */
export function reconcile(fees: BilledFee[], deductions: Deduction[]): ReconciliationException[] {
  const byAccount = indexDeductions(deductions);
  const exceptions = fees.map((f) => classify(f, byAccount.get(f.accountNumber))).filter((e): e is ReconciliationException => e !== null);
  const billed = new Set(fees.map((f) => f.accountNumber));
  for (const d of deductions) {
    if (!billed.has(d.accountNumber)) exceptions.push({ accountNumber: d.accountNumber, kind: 'not-found', difference: -d.amount });
  }
  return exceptions;
}
