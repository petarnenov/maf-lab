# Contoso Advisors Invoice Format

## Invoice Layout

Contoso Advisors invoices are produced by the TAMP platform using the firm's branded template. Each invoice is issued per household, not per account, because the flat fee is a household charge. The header shows the Contoso Advisors name and address, the household name, the invoice number, the invoice date, and the billing period covered. The body lists the fee schedule code, normally CONTOSO-FLAT-100, followed by the installment amount and any adjustments. The footer explains that fees are billed quarterly in advance.

## Line Items

### Installment Line

The first line shows the quarterly installment of the annual flat fee. For legacy asset-based households, this line instead shows the household AUM, the rate, and the calculated fee.

### Adjustment Lines

Proration for new accounts, refunds for terminations, household rebalancing fee charges, and waivers each appear as separate lines with a short description. A waived rebalancing fee is shown with an amount of zero and the word "waived" so the client can see it was considered.

### Account Allocation Table

Below the totals, a table lists each account in the household with the amount debited from it. The amounts sum to the invoice total. Accounts not debited, such as zero-balance accounts, are omitted.

## Delivery

Invoices are published to the client portal and emailed as a notification without attachments. The operations lead approves publication after invoice review. Corrected invoices carry the original number with a revision suffix.
