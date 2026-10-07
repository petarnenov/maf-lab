# Portfolio Domain Overview

## What the Portfolio Domain Covers

The portfolio domain is the part of the TAMP platform that knows what a client owns and how that ownership compares with what the client should own. It holds the household and account structure used for investment management, the model portfolio each account follows, the holdings within each account grouped by asset class, and the daily market value of every position. On top of that data it measures drift against the model, proposes rebalancing trades, strikes the quarter-end valuation, and produces performance reports. Advisors use it to answer questions such as "is this account still on model?", "why did this household's value go up?" and "how did the portfolio perform against its benchmark?"

## Core Objects: Households, Accounts, Sleeves and Holdings

An account is the unit that is custodied and traded. A household groups the accounts of one family, trust or institution so that allocation and value can be viewed together. Each account is assigned to exactly one model portfolio at a time, and some accounts are divided into sleeves, which are sub-portfolios managed to their own model or by their own manager inside a single custodial account. Holdings are stored at the security level and rolled up by asset class, for example US equity, International equity, Core bonds, Alternatives and Cash. Every rolled-up figure carries an as-of date so that drift and valuation always refer to the same moment.

## Portfolio Workflows

The main workflows are model assignment, drift monitoring, rebalancing, cash-flow processing, valuation and performance reporting. Drift monitoring compares the actual weight of each asset class with its target weight and flags any account outside the model's tolerance band. Rebalancing turns that flag into proposed trades, which are approved and sent to the custodian. Contributions and withdrawals are recorded as external cash flows so that they are kept separate from investment results. At each quarter end the portfolio service fixes the market value of every account as the official quarter-end valuation.

## Relationship to Billing

The portfolio domain and the billing domain meet at exactly one point: the quarter-end valuation. The market value the portfolio service strikes for each account at quarter end is the billable AUM that the billing engine reads when it runs the fee calculation. Billing applies firm exclusions, household aggregation and the fee schedule to that figure; the portfolio side never computes a fee, never chooses a fee schedule and never issues an invoice. In the other direction, the billing engine never prices a security, never measures drift and never proposes a trade. When a question is about why a value, weight or return changed, it belongs to the portfolio domain. When it is about what the client was charged, it belongs to billing, and the answer starts from the quarter-end value the portfolio domain handed over.

## Tenant Separation

Every household, account and model portfolio belongs to one advisory firm. Model codes are firm-specific and a firm's advisors see only their own households, models and valuations. Platform-wide documentation, such as this overview, describes behavior that applies to every firm; firm-specific model lineups and rebalancing calendars are documented separately by each firm.
