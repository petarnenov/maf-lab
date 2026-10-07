# Rebalancing Policy

## Calendar and Threshold Rebalancing

The platform supports two triggers for rebalancing, and firms usually combine them. Calendar rebalancing reviews every account on a fixed schedule, commonly quarterly or semi-annually, and brings accounts back to target whether or not they are outside tolerance. Threshold rebalancing checks drift frequently and rebalances only accounts where at least one asset class has moved outside the model's tolerance band. Calendar rebalancing keeps allocations predictable; threshold rebalancing reacts to large market moves and avoids unnecessary trading in quiet periods. Each firm documents its own calendar and threshold cadence.

## Approval and Trade Generation

A rebalance starts as a proposal. The platform compares actual holdings with the model's target weights, calculates the trades needed to return each asset class to target, and presents the proposed orders with their estimated drift after trading. An advisor or portfolio manager with trading permission reviews and approves the proposal; firms may require a second approver for large trade lists or for institutional accounts. Approved orders are sent to the custodian, and executions flow back into holdings on the next valuation cycle. Proposals that are not approved within five business days expire and must be regenerated from current prices.

## Cash Buffers and Minimum Trade Sizes

The trade generator preserves a cash buffer so that pending withdrawals and custodian-debited charges can be paid without selling securities at short notice. The buffer is taken from the model's cash target, and a firm can reserve a larger amount for an account with a known upcoming distribution. Trades smaller than the firm's minimum trade size are suppressed, which means an account can remain slightly away from target after a rebalance without being outside tolerance.

## Tax-Aware Rebalancing

For taxable accounts, the trade generator can prefer selling lots with losses or long-term gains, avoid short-term gains where possible, and respect wash-sale windows. Tax-aware settings may leave drift partially corrected when full correction would realize a large gain; the proposal shows the remaining drift so the approver can decide. Retirement and tax-exempt accounts ignore these settings and rebalance fully to target.

## Rebalancing Does Not Change AUM

A rebalance moves value from one asset class to another inside the same account. Selling $200,000 of US equity and buying $200,000 of Core bonds changes weights but leaves the account's total market value where it was, apart from small trading costs and spreads. Because billable AUM is the account's total market value at quarter end, rebalancing trades do not by themselves change the advisory fee. A client whose fee changed between quarters should look at market movement, contributions and withdrawals, or a valuation correction; those are the only things that move total AUM. Where a firm charges a separate rebalancing-related fee, that charge is defined in the firm's billing setup, not by the rebalance itself.
