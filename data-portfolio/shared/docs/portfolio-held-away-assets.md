# Held-Away Assets

## What Held-Away Assets Are

Held-away assets are investments a household owns that are not custodied in accounts the advisory firm manages on the platform. Common examples are a 401(k) at an employer's recordkeeper, a brokerage account at another firm, an annuity, a private business interest or real estate. The platform can bring these into the household view through aggregation feeds or manual entry so the advisor can see the family's complete balance sheet, but the firm does not have trading authority over them.

## How They Appear in the Household View

Held-away accounts are shown in a separate section of the household view, labeled as outside assets, with the data source and the date of the last update. They are rolled into a "total household wealth" figure, and the household allocation chart can be displayed with or without them. Values from aggregation feeds are refreshed as often as the source allows, often daily; manually entered values are only as current as their last entry and are marked stale after ninety days.

## Excluded from Drift and Rebalancing

Held-away accounts are not assigned a model portfolio, are not included in drift monitoring and are never included in a rebalance proposal. The firm cannot trade them, so measuring them against a tolerance band would produce flags nobody can act on. Where an advisor wants the managed accounts to compensate for a held-away position, for example by underweighting US equity because the client's 401(k) is fully in a US equity index fund, that is done by choosing a model or a custom sleeve for the managed accounts, not by pulling the outside asset into drift calculations.

## Held-Away Assets and Billable AUM

Held-away assets are not part of the quarter-end AUM that the portfolio service hands to billing unless the firm's billing setup explicitly includes them. Some firms bill on advised outside assets under a separate agreement, and in that case the billing engine reads a designated held-away value; others use outside assets only to help a household reach a fee breakpoint without charging on them. Both choices are made in billing configuration. Absent such a setting, a household's held-away assets do not affect its fee.

## Data Quality Considerations

Outside values are less reliable than custodied positions: aggregation feeds can break, statements can lag by a month, and asset classification is inferred rather than known. Performance reports therefore exclude held-away assets from time-weighted return by default and show them only as a value line, so an unexplained jump in an outside account does not distort the managed portfolio's reported results.
