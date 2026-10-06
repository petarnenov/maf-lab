// Acme Wealth Partners: client invoice formatting.

export interface InvoiceLine {
  accountId: string;
  averageAum: number;
  scheduleCode: string;
  fee: number;
}

export interface CreditLine {
  amount: number;
  clientExplanation: string;
}

const currency = new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" });

/** Plain-language description shown instead of the rate table. */
export function describeSchedule(code: string): string {
  if (code === "ACME-TIER-2026") {
    return "Tiered annual rate from 1.00% to 0.45% based on household assets";
  }
  return `Fee schedule ${code} as described in your fee disclosure`;
}

/** Strips internal codes (run ids, ticket numbers) from credit explanations. */
export function sanitizeExplanation(text: string): string {
  return text.replace(/\b(RUN|TKT|ACME)-[A-Z0-9-]+\b/g, "").replace(/\s{2,}/g, " ").trim();
}

/** Formats the invoice body for the client portal. */
export function formatInvoice(household: string, period: string, lines: InvoiceLine[], credits: CreditLine[]): string {
  const body = lines.map(
    (l) => `${l.accountId}  ${currency.format(l.averageAum)}  ${describeSchedule(l.scheduleCode)}  ${currency.format(l.fee)}`,
  );
  const gross = lines.reduce((s, l) => s + l.fee, 0);
  const creditTotal = credits.reduce((s, c) => s + c.amount, 0);
  const creditLines = credits.map((c) => `Credit: ${sanitizeExplanation(c.clientExplanation)}  -${currency.format(c.amount)}`);
  return [`Acme Wealth Partners`, `${household} — ${period}`, ...body, ...creditLines, `Total due: ${currency.format(gross - creditTotal)}`].join("\n");
}
