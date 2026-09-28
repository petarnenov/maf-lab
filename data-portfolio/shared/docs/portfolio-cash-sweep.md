# Cash and the Cash Sweep

## Cash as an Asset Class

On the platform, cash is a full asset class with its own target weight in each model portfolio. It is included in drift calculations exactly like equity or bonds: an account holding 9% cash against a 5% target has +4 points of cash drift. Treating cash as an asset class prevents idle balances from quietly accumulating, and it gives the rebalancer a defined source of funds for purchases and a defined buffer for withdrawals.

## Sweep Vehicles

Uninvested cash in a custodial account is moved automatically each night into the custodian's sweep vehicle. Depending on the custodian and the firm's election, the sweep is a bank deposit program, a government money market fund or a treasury-only money market fund. The platform reports the sweep as Cash regardless of vehicle, and it records the sweep yield so that performance reports show income earned on cash. Money market funds bought deliberately as a position, rather than through the sweep, are also classified as Cash unless the firm maps them elsewhere.

## Cash Targets in Models

Cash targets vary by model purpose. Growth models commonly target 0 to 5 percent, balanced models around 5 percent, and income or conservative models 10 to 15 percent to fund regular distributions. The target is part of the model definition and changes with it. A firm can add an account-level cash reserve on top of the model target for a known upcoming payment; the reserve is excluded from the cash drift calculation until its release date so that it does not trigger a rebalance.

## Does Cash Count in AUM?

Cash, including the sweep balance, is part of the account's total market value and is therefore included in the quarter-end AUM handed to billing. Whether that cash is charged a fee is a billing decision: some firms exclude cash, or cash above a threshold, from billable AUM in their billing setup. The portfolio service always reports the full cash balance; any exclusion is applied afterward by the billing engine and shown on the invoice.

## Cash Drag and Monitoring

Holding more cash than the model calls for creates cash drag: in rising markets, the account underperforms its model because part of it earns only the sweep yield. The daily drift check flags accounts with positive cash drift beyond tolerance, and the monthly cash report lists accounts where cash has been above target for more than thirty days, which often indicates a contribution that was never invested.
