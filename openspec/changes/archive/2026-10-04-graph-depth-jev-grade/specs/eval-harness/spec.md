# Spec Delta

## MODIFIED Requirements

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
- faithfulness and relevance from the Jev grade the generation suite uses: faithfulness over the answer's claim
  sentences against what the turn read, cited places checked in code, and relevance as whether the answer addresses the
  question;
- the mean share of needed items the answer names;
- the share of turns that called a graph tool at all.

A turn that called no graph tool SHALL still be scored, and SHALL be counted in the tool-call share, so a variant cannot
look better by being ignored. Judge failures SHALL be counted and reported apart from the scores. A case SHALL fail the end-to-end layer when its
faithfulness is below 0.75, its relevance is not 1, or it called no graph tool. The end-to-end layer SHALL refuse to run
without the Jev key; the structural layer SHALL need neither the chat model nor the Jev key.

Because the pinned depth can only matter in a turn that called a graph tool, the end-to-end layer SHALL also report,
beside the all-turn scores:
- each variant's faithfulness, relevance and mention recall over only its own turns that called a code graph tool, with
  the number of such turns;
- the same three scores over only the common cases: those whose turns called a code graph tool in every variant of the
  run, with the number of such cases, so the variants are compared on the same questions;
- mention recall over the common cases, split by the depth each case needs.

A score over an empty set SHALL be absent rather than zero. The report SHALL carry each case's graph-call outcome per
variant.

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

#### Scenario: Scores over the graph turns
- **WHEN** a variant's agent called a graph tool in 18 of 24 turns
- **THEN** that variant reports its scores over those 18 turns beside the all-turn scores, and reports 18 as its graph-turn count

#### Scenario: The common cases
- **WHEN** 15 cases called a graph tool in all three variants
- **THEN** every variant reports its scores over those 15 cases, and 15 as the common-case count

#### Scenario: No common case
- **WHEN** no case called a graph tool in every variant
- **THEN** the common-case scores are absent, the common-case count is 0, and the run still passes

#### Scenario: An answer graded by Jev
- **WHEN** the end-to-end layer grades an answer that names a caller no source the turn read holds
- **THEN** that sentence counts as unsupported, the case's faithfulness drops, and the reason quotes the sentence

#### Scenario: No Jev key for the end-to-end layer
- **WHEN** the suite runs both layers without the Jev key
- **THEN** it refuses to run and names the missing variable; with the structural-only option it runs
