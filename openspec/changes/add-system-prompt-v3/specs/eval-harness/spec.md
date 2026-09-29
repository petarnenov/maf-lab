# Spec Delta

## ADDED Requirements

### Requirement: How answers present card data is measured
A `presentation` suite SHALL run portfolio questions from a JSONL dataset through the agent. Each row carries an id, a
question, a firm, and whether the turn is expected to show a card. A rebalance question also carries the expected
verdict: needed or not needed.

The suite SHALL report:
- **`noTable`:** the share of carded turns whose answer contains no markdown table (two or more pipe-delimited lines).
- **`noRowList`:** the share of carded turns whose answer does not list the card's rows one by one (three or more list
  lines, each naming a different row: an asset class, an account id, a quarter end). A row marked `rowsRequested`,
  whose question explicitly asks about every row, is left out of this metric.
- **`figuresGrounded`:** the share of currency amounts in the answers that appear in the content of that turn's cards.
- **`verdictCorrect`:** the share of rebalance questions whose answer states the expected verdict. It SHALL be judged
  by a yes/no rubric.
- **`languageMatch`:** the share of answers written in the question's language, read from the script most of the
  answer's letters are in.

Each case below target SHALL be listed with its reason. The suite SHALL have thresholds and a baseline like the other
suites, and SHALL be runnable alone (`SUITE=presentation`) and as part of `all`. The dataset SHALL include English and
Bulgarian questions.

#### Scenario: A restated table is counted
- **WHEN** an answer to a carded question contains a markdown table
- **THEN** that case lowers `noTable` and is listed with the reason "table in answer"

#### Scenario: Rows listed one by one are counted
- **WHEN** an answer to a holdings question has a bullet for US equity, one for international equity and one for cash
- **THEN** that case lowers `noRowList` and is listed with the reason "card rows listed one by one"

#### Scenario: An invented amount is counted
- **WHEN** an answer quotes "8 500 $" and no card in that turn contains 8,500
- **THEN** that amount lowers `figuresGrounded`, and the case is listed with the amount
