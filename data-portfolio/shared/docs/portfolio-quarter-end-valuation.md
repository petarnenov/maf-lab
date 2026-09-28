# Quarter-End Valuation

## Striking the Quarter-End Market Value

At the close of the last business day of each calendar quarter, the portfolio service strikes an official market value for every managed account. It takes the positions reported by the custodian for that date, applies closing prices, adds accrued income where the firm includes it, and sums the result by asset class and in total. The resulting figure is locked as the quarter-end valuation, stamped with the as-of date, and becomes the reference for the quarter-end drift report, the quarterly performance report and the handoff to billing.

## Pricing Sources and Hierarchy

Listed equities and exchange-traded funds are priced at the primary exchange close. Mutual funds use the published net asset value for the date. Bonds use evaluated prices from the platform's fixed income pricing vendor, falling back to the custodian's price when the vendor has none. Cash and sweep balances are valued at par. Alternatives and other illiquid holdings use the most recent manager-reported value, and the valuation records the date of that report so the lag is visible. When two sources disagree by more than the firm's tolerance, the position is placed on a pricing exception list for review before the quarter-end value is locked.

## Corrections Before and After the Handoff

A price that is found to be wrong before billing runs is corrected in place, with the original price, the corrected price and the reason retained in the valuation history. A correction discovered after billing has already read the value creates a new version of the quarter-end valuation instead of overwriting the old one, so the figure used by the billing run remains reproducible. Any correction to a quarter-end value must be coordinated with the billing desk, because billing may need to rerun or issue an adjustment.

## Handoff to Billing

Once the quarter-end valuation is locked, the portfolio service publishes it as the quarter-end AUM for each account. The billing engine reads that figure as the billable AUM for the period, applies the firm's exclusions and household aggregation, and calculates the fee. The portfolio service does not know which fee schedule applies and does not calculate fees; it only guarantees that the value it hands over is complete, priced and final for the date.

## Why a Fee Changes When AUM Crosses a Band

Households on tiered or breakpoint fee schedules pay different rates on different bands of assets. When a household's quarter-end AUM rises or falls across a band boundary, the portion of assets in each band changes and so does the blended rate, even if the change in value is modest. A household that grows from $2.9 million to $3.2 million, for example, may have its first dollars above a boundary charged at a lower marginal rate. The band boundaries and rates are defined in each firm's fee schedule; see the billing documentation on tiered fee calculation for how bands are applied.
