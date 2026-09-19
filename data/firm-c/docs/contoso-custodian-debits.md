# Custodian Fee Debits

## How Fees Are Collected

After a Contoso Advisors billing run is approved, the platform generates a fee file for the custodian. The file lists each account and the amount to debit. The custodian processes the file within two business days and deducts the fee from the cash balance of each account. If an account lacks sufficient cash, the custodian may sell money-market shares according to the client's sweep instructions. Contoso Advisors does not sell other securities to pay fees unless the client has agreed in writing.

## Reconciliation

### Matching Debits

The operations associate downloads the custodian's fee report after processing and matches each debit to the platform invoice for the same account. Matching is done by account number and amount. Unmatched debits usually mean an account was closed or transferred between the run and the custodian processing date.

### CUSTODIAN-MISMATCH Warnings

If the platform detects that an account's custodian record does not match the billing record, for example a different registration or a closed status, it flags CUSTODIAN-MISMATCH. The associate resolves each warning by updating the account link or excluding the account from the fee file, and re-submits the corrected file if needed.

## Rejected Debits

Occasionally the custodian rejects a debit, for instance when an account is restricted. Rejected debits are listed on the custodian's exception report. The operations lead decides whether to re-bill the amount from another household account or to invoice the client directly. The decision is recorded in the household notes.
