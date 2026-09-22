# Design

## Context

See proposal.md — Why. Three facts from the code shape this.

**One number reaches the gate.** `Program.cs` calls
`RegressionGate.Compare(baseline, variants, options.ToleranceFor(name))`, and `Compare` applies that
scalar to every metric of every variant. The resolution happens once, before the gate knows which
metric it is judging.

**The escape hatch exists and is already in use.** `EvalOptions.RegressionTolerances` maps a suite
name to a number, `retrieval` is set to 0.03 in `eval.json`, and the field's own comment explains
why: a non-English query is translated by a live model, and a different translation retrieves
different chunks. The mechanism is right; the granularity is one level too coarse.

**The noise was measured, not guessed.** Today's verification produced repeated runs of both
suites. `recall@5:bg` moved 0.041 across runs while `recall@5:en` did not move at all;
`faithfulness` moved 0.094 across eleven runs while `sourceRecall` did not move at all. Those are
the numbers this change is built on, and they are why a per-suite value cannot be right for both
metrics in either suite.

## Goals / Non-Goals

**Goals:**

- A metric is judged against its own noise.
- A stable metric stays strict even when it shares a suite with a noisy one.
- Every tolerance in configuration can be traced back to a measurement.

**Non-Goals:**

- Reducing the noise. The translation variance and the judge variance are real and worth attacking,
  but each is its own change with its own evidence. This one stops the gate from lying about them.
- Changing any metric, dataset, threshold or baseline value.
- Per-variant tolerances. The noise comes from how a metric is produced, which is the same across
  `hybrid`, `dense` and `sparse`; adding a third level would be granularity nobody asked for.

## Decisions

### A list of entries, because a metric name cannot be a configuration key

The first attempt kept `RegressionTolerances` as `Dictionary<string, double>` and added keys of the
form `suite/metric`. It does not work, and the test written for it is what proved it: configuring
`Evals:RegressionTolerances:retrieval/recall@5:bg` resolved to the *suite's* 0.03, not the
metric's 0.05.

`:` is the configuration path separator, and metric names contain one — `recall@5:bg`. The key is
split into segments, so the dictionary receives `retrieval/recall@5` with a nested `bg` rather than
the flat key intended. The JSON provider flattens property names the same way, so putting it in
`eval.json` does not help. No separator choice fixes this, because the offending colon is inside the
metric's own name.

So `RegressionTolerances` becomes a list of entries — suite, optional metric, the tolerance, and
what it was measured from. A name that is a *value* has no separator problem. The shape also gives
the provenance the spec requires a real field instead of a comment that the next reader can ignore:

```json
{ "Suite": "retrieval", "Metric": "recall@5:bg", "Tolerance": 0.05, "Measured": "0.660–0.701 over 5 runs" }
```

An entry with no metric applies to the whole suite, so the two levels live in one list rather than
two shapes. Resolution is: the entry for this suite and metric, then the entry for this suite with
no metric, then the default.

Alternative considered: a nested `Dictionary<string, Dictionary<string, double>>`. The inner key
still contains `:` and still splits, so it does not solve the problem it looks like it solves.

Alternative considered: rewriting `:` to a safe character in the key and translating on lookup. The
key then stops being the metric's name, and whoever writes one has to know an undocumented rule.

### The gate resolves per metric, so it takes a resolver

`Compare` takes `Func<string, double>` instead of `double`, and asks it for each metric as it
compares. That is the smallest change that moves the decision to where the metric name is known.

Alternative considered: pass the whole `EvalOptions` and the suite name into `Compare`. It would
work, but it hands a reporting helper the configuration object and the job of interpreting it;
the resolver keeps `RegressionGate` about comparing.

### A tolerance carries what it was measured from

Each configured tolerance carries a `Measured` field naming the observed spread and the number of
runs behind it. This is the part that decays first: a number with no provenance gets widened by the
next person who sees a red gate, and then the gate means nothing.

The rule that makes this work is the one in the spec delta: a tolerance is derived from measured
spread. A tolerance that makes today's run pass is not a tolerance, it is an accepted regression
wearing one.

### What the numbers will be

Not decided here. The proposal's table is what today's verification happened to produce, from runs
made for other reasons — enough to prove a per-suite number cannot fit, not enough to set a value.
The tasks measure each suite's metrics over repeated runs first, and the tolerances come from that.

If a metric turns out to be so noisy that any tolerance covering it would also cover a real
regression — `faithfulness` at 0.094 over an 8-case dataset is the candidate — the honest outcome is
to say so rather than to configure a number that makes the gate decorative. That metric's problem is
its sample size, not its tolerance.

## Risks / Trade-offs

- **A per-metric tolerance is a place to hide a regression.** Someone can widen one metric instead
  of fixing it. → It is narrower than what exists today, which lets someone hide a regression in
  every metric of a suite at once, and the spec now requires the number to come from a measurement.
- **Stable metrics get stricter, so some runs that pass today will fail.** That is intended and
  should be expected on the first run after this lands. → The tasks measure before setting values,
  so a metric is only made strict once its stability is observed rather than assumed.
- **The measurement is of this machine, this corpus, this day.** Noise may differ on CI. → The
  numbers are recorded with what produced them, so a later disagreement is a comparison rather than
  an argument.

## Migration Plan

One configured entry exists — `retrieval: 0.03` in `eval.json` — and it is rewritten into the new
shape in the same change, so there is no window in which configuration is read wrongly. The shape is
not backward compatible, and deliberately so: a dictionary entry left behind would bind to nothing
and silently fall back to the default, which is the kind of quiet failure this change exists to
remove. `eval.json` is the only file that configures these, and it lives in the repository.

A run with nothing configured behaves exactly as today.

## Measured spread

Five consecutive runs of every suite, nothing else changed between them. Every metric each suite
reports, including the ones that do not move.

| suite | variant | metric | min | max | range |
|---|---|---|---|---|---|
| generation | agent | `faithfulness` | 0.8750 | 1.0000 | **0.1250** |
| selection | agent | `exactMatch` | 0.9167 | 1.0000 | **0.0833** |
| selection | agent | `precision` | 0.9259 | 1.0000 | **0.0741** |
| generation | agent | `relevance` | 0.9688 | 1.0000 | **0.0312** |
| retrieval | hybrid | `recall@5:bg` | 0.6806 | 0.7014 | **0.0208** |
| retrieval | hybrid | `recall@20` | 0.9218 | 0.9422 | **0.0204** |
| retrieval | hybrid-dbsf | `mrr` | 0.5748 | 0.5860 | 0.0112 |
| retrieval | hybrid | `mrr` | 0.6387 | 0.6489 | 0.0102 |
| retrieval | hybrid | `recall@5` | 0.6871 | 0.6973 | 0.0102 |
| retrieval | dense | `recall@20` | 0.8980 | 0.9082 | 0.0102 |
| retrieval | dense | `mrr` | 0.6869 | 0.6879 | 0.0010 |
| retrieval | * | `recall@5:en` | — | — | **0.0000** |
| retrieval | sparse | every metric | — | — | **0.0000** |
| retrieval | * | `offDomainSilence` | — | — | 0.0000 |
| generation | agent | `sourceRecall` | 0.8750 | 0.8750 | 0.0000 |
| selection | agent | `recall`, `negativeAccuracy` | — | — | 0.0000 |
| injection | agent | `passRate` | — | — | 0.0000 |
| confirmation | agent | `faithfulness` | — | — | 0.0000 |

**Two numbers in the proposal were wrong, and this corrects them.** `recall@5:bg` was quoted at
~0.041; over five consecutive runs it is **0.0208**. The larger figure came from runs taken hours
apart across a rebuilt stack and a changed classifier — a comparison of different systems, not of
the same system twice. `faithfulness` was quoted at ~0.094; it is **0.1250**, worse than estimated.

**And one suite was missed entirely.** `selection` was never mentioned as noisy. Its `exactMatch`
moves 0.0833 and its `precision` 0.0741 — four times the default tolerance, and more than anything
in retrieval. It has no per-suite tolerance today, so its gate is as unreliable as generation's; it
simply had not been run enough times in a row for anyone to see it.

The pattern the change was built on holds, and holds harder than argued: within one suite,
`recall@5:bg` moves 0.0208 while `recall@5:en` does not move at all, and the whole `sparse` variant
is perfectly stable while `hybrid` is not. Noise follows how a metric is produced — a live
translation, a model judging prose — and never the suite that reports it.

## Which tolerances were set, and which were refused

Set, as the observed range rounded up to the next 0.005:

| suite | metric | range | tolerance |
|---|---|---|---|
| retrieval | `recall@5:bg` | 0.0208 | 0.025 |
| retrieval | `recall@20` | 0.0204 | 0.025 |
| generation | `relevance` | 0.0312 | 0.035 |

The retrieval suite-wide 0.03 is gone. Every other retrieval metric now answers to the 0.02
default, which each of them clears with room — the widest is `hybrid-dbsf`'s `mrr` at 0.0112, and
`recall@5:en` and the whole `sparse` variant do not move at all. Under the old arrangement all of
them were allowed 0.03.

**Refused, and this is the part worth reading.** Three metrics move more than the default and got
nothing, because a tolerance wide enough to cover their noise would also cover a regression worth
catching:

- `generation/faithfulness`, range **0.1250** over 8 cases. One case is worth exactly 0.125, so a
  covering tolerance says "any single case may break completely and the gate will not mention it".
- `selection/exactMatch`, range **0.0833** over 24 cases, which is two cases.
- `selection/precision`, range **0.0741**, the same two cases seen from the other side.

Their gates therefore stay unreliable, and that is the honest report rather than a fix. The problem
is not the tolerance, it is that the datasets are small enough that ordinary judge variance and one
flipped case are the same size. The remedy is more cases — a separate change, with its own evidence
to gather — and configuring a number here would have hidden the need for it behind a green run.
