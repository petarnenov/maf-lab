# Spec Delta

## ADDED Requirements

### Requirement: Data cards in the chat
The chat SHALL render each data card of a turn as a table in that turn, where the card arrived. A card that arrives
before the answer text SHALL be shown at once. The card SHALL NOT wait for the answer.

**The three card types:**
- **Holdings** (`maf-lab/holdings`) SHALL show:
  - one row per asset class: market value, target weight, actual weight, and drift with the tolerance band drawn;
  - the trade to target, labelled buy or sell in words, and the weight after;
  - a total row;
  - a badge saying whether a rebalance is needed.
  - A class outside the tolerance SHALL be marked by text or an icon as well as by colour.
- **AUM history** (`maf-lab/aum-history`) SHALL show one row per quarter end, with its AUM and its change from the
  previous quarter.
- **Accounts** (`maf-lab/accounts`) SHALL show one row per account: its id, name, household, model portfolio and
  currency.

**Every card SHALL:**
- be a table with a caption naming the account, or the user's accounts, and the as-of date where there is one;
- align numbers right, in tabular figures;
- format amounts and percentages for the language of the turn's question: Bulgarian when it is written in Cyrillic,
  English otherwise. Amounts use the card's currency.
- offer to copy its rows as CSV;
- scroll horizontally inside itself at phone width, without scrolling the page;
- read correctly in both light and dark themes.

**Other rules:**
- A card of an unknown type SHALL be ignored.
- When a turn is replayed to a step (time travel), only the cards that had arrived by that step SHALL be shown.

#### Scenario: A holdings card before the answer
- **WHEN** the portfolio tool for A-1043 returns while the model has not yet written anything
- **THEN** the holdings table appears in the turn with four classes, their trades and a "no rebalance needed" badge,
  before any answer text

#### Scenario: Bulgarian formatting
- **WHEN** the question was "Препоръчай ребалансиране за A-1043"
- **THEN** amounts read like "268 000 $" and percentages like "20,6 %"

#### Scenario: Buy and sell are words, not only colours
- **WHEN** the plan sells US equity and buys cash
- **THEN** the rows say "Продажба" and "Покупка", or "Sell" and "Buy" in English, next to the amounts

#### Scenario: Copy as CSV
- **WHEN** the user presses "Copy as CSV" on a card
- **THEN** the clipboard receives a header row and one row per table row, with plain numbers

#### Scenario: An unknown card type
- **WHEN** an activity of type `maf-lab/something-new` arrives
- **THEN** nothing is rendered for it and the turn still completes

#### Scenario: Time travel before the card
- **WHEN** a turn is replayed to a step before its portfolio tool returned
- **THEN** the holdings card is not shown, and it appears at the step where it arrived
