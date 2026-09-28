# Design

## Context

`MetricTrend` builds one series per `suite/variant/metric` from the report summaries and renders them as flat
`<option>`s in a native `<select>`, sorted by run count then id. Series ids come from the reports: suites and variant
names are single path segments; per-language and per-split breakdowns are written by the eval harness as
`metric:part` (`recall@5:bg`, `accuracy:holdout`). The chosen series is component state keyed by the full id; there is
no URL state.

## Goals / Non-Goals

**Goals:**
- A pure, unit-tested function turns the series ids into ordered groups; the component only renders them.
- Native `<optgroup>`s, so keyboard type-ahead, screen-reader group announcement and the platform picker keep working.

**Non-Goals:**
- A custom listbox or a searchable combobox.
- Changing which series exist, how points or baselines are computed, or the chart.

## Decisions

- **Split the id at the first two `/`**: suite, variant, and the rest as the metric; the metric at the first `:` into
  base and breakdown. Anything missing a segment falls back to an empty variant rather than being dropped.
- **Suite and variant order are small preference lists, not the metric catalogue.** Known suites rank in pipeline order
  (selection → retrieval → generation → injection → confirmation → intent → a2a-conformance); retrieval's variants rank
  hybrid (production), hybrid-dbsf, dense, sparse. Anything unlisted sorts after the listed ones alphabetically, so a
  new suite or variant still gets its own group. Alternative considered: alphabetical everywhere — rejected, it puts
  a2a-conformance first and dense before hybrid, burying the production numbers.
- **Metric order is derived from the ids.** A base metric that has breakdowns is the one worth breaking down, so it
  leads; the rest of its family (same text before `@`, compared by the number after it, so `recall@5` < `recall@20`)
  follows; everything else is alphabetical. This yields recall@5, recall@20, mrr, offDomainSilence for retrieval and
  accuracy, forcedWhenShould, unforcedWhenShouldNot for intent, with no metric named in code. Alternative considered:
  "gated metrics first" using thresholds — rejected because only the gated variant (hybrid) carries thresholds, so the
  same metrics would order differently per variant.
- **Breakdowns**: languages first — en, bg, bg-latn, then any other language-code-shaped part (two letters plus optional
  `-subtag`) alphabetically — then other splits alphabetically.
- **Labels**: group `suite · variant`; option `metric` or `metric · part`. The option `value` stays the full id, so the
  state and the chart's accessible name (`<id> over time`) are unchanged. Because a closed native select shows only the
  option text, the chosen series' group is shown as text next to the picker and referenced by `aria-describedby`.
- **Default selection unchanged**: with nothing chosen, the series with the most runs (ties by id) is shown, as today —
  grouping changes the list, not what the card opens on. A chosen id that is still present stays chosen across refetches
  (it is kept by id, not by index).
- **Phone width**: the picker gets `max-width: 100%` and `min-width: 0` inside the wrapping header, so a long option can
  never widen the page; shorter option labels already make it narrower. Measured against the live reports at 375 px,
  the picker already fit (37–264 px of a 20–355 px card), but the runs table (metrics column) reached 533 px and
  scrolled the whole page; it is wrapped in a `scroll` container (`Page.module.css`) so it scrolls inside itself.
  The shared header's persona `<select>` (long firm labels) also reaches 540 px; that is the app shell, not this
  screen, and is left for a separate change.

## Layout finding

At a 2400 px viewport the `/evals` content is a 1100 px column centred by `Page.module.css` (`max-width: 1100px;
margin: 0 auto`): main spans 0–2385, the page column 643–1743, so the margins are equal on both sides and nothing is
pushed right. The chart is capped at 560 px and left-aligned inside the full-width Trend card, which leaves the card's
right half empty — that is the only asymmetry, and it is intentional (a readable chart size). No layout change is made.

## Risks / Trade-offs

- [A metric with `:` meaning something other than a breakdown] → it still groups under its base and sorts among the
  breakdowns; harmless, and the full id is still the value.
- [A split name that looks like a language code, e.g. `ab`] → it sorts with languages; acceptable, ordering only.
