# Drift and Tolerance Bands

## Measuring Drift

Drift is the difference between an asset class's actual weight in an account and its target weight in the assigned model portfolio, expressed in percentage points. Actual weight is the market value of the asset class divided by the total market value of the account on the measurement date. If a model targets 40% US equity and the account holds 46.9% US equity, drift for that asset class is +6.9 points. Negative drift means the account is underweight; positive drift means it is overweight. Drift is always measured in points, never as a relative percentage of the target, so a 5-point band means the same thing for a 5% cash target as for a 60% bond target.

## Tolerance Bands

Every model portfolio carries a drift tolerance, typically between 3 and 7.5 percentage points. The tolerance defines a band around each target weight: with a 5-point tolerance, a 40% target has a band from 35% to 45%. The same tolerance applies to every asset class in the model. Models for institutional mandates with tight liability matching tend to use narrower bands, while growth models with volatile asset classes use wider bands to avoid trading on every market swing.

## What Outside Tolerance Means

An account is outside tolerance when the absolute drift of at least one asset class exceeds the model's drift tolerance. A drift exactly equal to the tolerance is still inside the band. Being outside tolerance is an operational flag, not an error: it tells the advisor that the account's risk profile has moved away from what the client agreed to and that a rebalance should be considered. Drift reports list every asset class outside its band, the size of the breach and the direction, and they rank accounts by their largest absolute drift so the most off-model accounts are reviewed first.

## Drift as of the Valuation Date

Drift is measured as of a specific valuation date, using the market values stored for that date. It is never computed from a mixture of prices from different days. The daily drift check uses the previous business day's valuation, and the quarter-end drift report uses the official quarter-end valuation, so the weights shown on the report reconcile exactly to the market values that were handed to billing. If a price for the valuation date is later corrected, drift for that date is recalculated and the earlier report is kept as a superseded version.

## What Drift Does Not Tell You

Drift describes the shape of the portfolio, not its size. An account can be perfectly on model while losing value in a falling market, and an account can be far outside tolerance while its total market value is unchanged. For questions about why total value changed, look at market movement and cash flows rather than drift.
