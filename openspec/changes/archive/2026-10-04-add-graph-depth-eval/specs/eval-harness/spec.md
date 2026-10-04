# Spec Delta

## ADDED Requirements

### Requirement: Graph depth dataset
The harness SHALL read `evals/graph-depth.jsonl`, one case per row. A case SHALL be either a trace or an impact:
- A trace names a code symbol and a direction, callers or callees.
- An impact names a repository-relative C# file.

Every case SHALL carry:
- the question a developer would ask about it;
- the items an answer needs: method symbols for a trace, test files for an impact;
- for each needed item, the number of calls at which it is first reached.

A needed item that is not reachable within the deepest variant SHALL still be listed and marked as beyond reach. A
missing caller is then reported, not silently dropped. A row without needed items SHALL be rejected when the dataset
loads.

#### Scenario: A trace case
- **WHEN** the harness loads a trace row
- **THEN** the case carries its symbol, its direction, its question, and each needed method with the number of calls at which it is reached

#### Scenario: An impact case
- **WHEN** the harness loads an impact row
- **THEN** the case carries its file, its question, and each needed test file with the number of calls at which it is reached

#### Scenario: A row that lists nothing needed
- **WHEN** a row declares no needed items
- **THEN** loading the dataset fails and names the row

### Requirement: Graph depth comparison
The `graph-depth` suite SHALL run every case once for each of three variants: `depth-2`, `depth-3` and `depth-4`. In
each variant the code graph tools are pinned to that depth. It SHALL measure two layers.

The structural layer calls the graph tools directly, with no model. It SHALL report per variant:
- the mean share of needed items reached;
- the share of cases with every needed item reached;
- the same recall split by the depth each case needs, so a gain for deep cases is not hidden by an average over shallow
  ones;
- the share of returned nodes that were needed;
- the mean number of returned nodes;
- the mean size of the tool result in tokens;
- the share of results that were truncated;
- tool latency at p50 and p95.

The end-to-end layer asks the agent each case's question. It SHALL report per variant:
- faithfulness and relevance from the fixed rubric judge, scored against the case's needed items;
- the mean share of needed items the answer names;
- the share of turns that called a graph tool at all.

A turn that called no graph tool SHALL still be scored, and SHALL be counted in the tool-call share, so a variant cannot
look better by being ignored. Judge failures SHALL be counted and reported apart from the scores.

The suite SHALL be able to run the structural layer alone, with no chat model and no Jev key.

#### Scenario: Three variants side by side
- **WHEN** the graph-depth suite runs
- **THEN** the report holds `depth-2`, `depth-3` and `depth-4`, each with the structural and the end-to-end metrics

#### Scenario: A case that needs three calls
- **WHEN** a case's deepest needed item is reached at three calls
- **THEN** `depth-2` reports it as not fully reached, and `depth-3` and `depth-4` report it as fully reached

#### Scenario: Recall by required depth
- **WHEN** the dataset holds cases that need one, two, three and four calls
- **THEN** each variant reports recall separately for each required depth as well as overall

#### Scenario: Cost is reported beside recall
- **WHEN** a deeper variant reaches more nodes
- **THEN** the report shows its larger mean node count, token size and truncated rate next to its recall

#### Scenario: Structural only
- **WHEN** the suite runs with the structural-only option and no chat model key is set
- **THEN** it reports the structural metrics for all three variants and does not start a chat turn

#### Scenario: The model skips the graph
- **WHEN** the agent answers a case without calling a graph tool
- **THEN** the case is still judged, and the variant's tool-call share is lower

### Requirement: Comparison suites do not gate
A suite whose purpose is to compare settings, starting with `graph-depth`, SHALL be reported in the same JSON and
Markdown shape as every other suite, but SHALL NOT gate:
- it has no thresholds;
- it is not compared with the baseline;
- asking to accept a baseline SHALL leave its entry absent;
- running all suites SHALL NOT include it, and it SHALL run only when named.

Its run SHALL fail only when it could not run: an invalid dataset, a tool or server that could not be reached, or a
pin the server refused.

#### Scenario: Not in all
- **WHEN** the harness runs with `all` suites
- **THEN** graph-depth does not run

#### Scenario: Never in the baseline
- **WHEN** graph-depth runs with the baseline accept option
- **THEN** the baseline file has no graph-depth entry afterwards

#### Scenario: A worse number does not fail
- **WHEN** a variant's recall is lower than in the previous run
- **THEN** the run still passes, and the report shows the numbers

#### Scenario: It cannot run
- **WHEN** the code graph server cannot be reached
- **THEN** the run fails and says which variant could not start

### Requirement: Graph depth progress
The graph-depth suite SHALL show one determinate progress bar over every case of every variant it runs. The bar SHALL
read done/total and a percentage, and SHALL follow the progress-feedback rules for output that is not a terminal and
for the final line. Progress lines SHALL carry case ids and counts, never a question or an answer.

#### Scenario: A terminal run
- **WHEN** a developer runs `make eval SUITE=graph-depth` in a terminal
- **THEN** one bar advances with each finished case of each variant and reads done/total and a percentage

#### Scenario: Output redirected
- **WHEN** the output is not a terminal
- **THEN** the suite prints plain progress lines at a bounded rate, with no control characters
