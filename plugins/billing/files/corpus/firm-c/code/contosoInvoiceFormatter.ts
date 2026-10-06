// Contoso Advisors household invoice formatter.
// One invoice per household; flat fee installments billed quarterly in advance.

export interface InvoiceLine {
  description: string;
  amount: number; // negative for credits
  waived?: boolean;
}

export interface HouseholdInvoice {
  invoiceNumber: string;
  revision?: number;
  householdName: string;
  scheduleCode: string; // usually CONTOSO-FLAT-100
  periodStart: string; // yyyy-MM-dd
  periodEnd: string;
  lines: InvoiceLine[];
  allocations: Record<string, number>; // accountId -> amount debited
}

const usd = new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" });

export function invoiceTotal(inv: HouseholdInvoice): number {
  return Math.round(inv.lines.reduce((s, l) => s + (l.waived ? 0 : l.amount), 0) * 100) / 100;
}

export function invoiceLabel(inv: HouseholdInvoice): string {
  // Corrected invoices keep the original number with a revision suffix.
  return inv.revision ? `${inv.invoiceNumber}-R${inv.revision}` : inv.invoiceNumber;
}

export function formatLine(line: InvoiceLine): string {
  const amt = line.waived ? `${usd.format(0)} (waived)` : usd.format(line.amount);
  return `${line.description.padEnd(48)} ${amt}`;
}

export function renderInvoice(inv: HouseholdInvoice): string {
  const header = [
    "Contoso Advisors",
    `Invoice ${invoiceLabel(inv)} - ${inv.householdName}`,
    `Schedule ${inv.scheduleCode}, period ${inv.periodStart} to ${inv.periodEnd} (billed in advance)`,
  ];
  const body = inv.lines.map(formatLine);
  const alloc = Object.entries(inv.allocations)
    .filter(([, amt]) => amt > 0)
    .map(([acct, amt]) => `  ${acct}: ${usd.format(amt)}`);
  return [...header, "", ...body, "", `Total: ${usd.format(invoiceTotal(inv))}`, "Debited from:", ...alloc].join("\n");
}
