# Spec Delta

## MODIFIED Requirements

### Requirement: Reports and thresholds
Each run SHALL write a JSON report and a Markdown summary readable by the web
app. Pass/fail thresholds SHALL come from configuration. The harness SHALL run
only on demand and be documented as required after changes to prompts, tool
descriptions, the model, the tool set, or chunking configuration.

The repository SHALL also hold a **baseline** of the metrics currently accepted, per suite and variant, so that what
"good" is today is stated where it can be reviewed rather than remembered. Every run SHALL compare its metrics with
that baseline and SHALL fail when a metric has dropped by more than a configured tolerance, naming the suite, the
variant, the metric, the baseline value, the new value and the size of the drop. A metric at or above its baseline,
or within the tolerance, SHALL NOT fail the run.

The tolerance a metric is compared against SHALL be that metric's. Configuration SHALL be able to set one for a
named metric of a named suite, and SHALL fall back to the suite's and then to the default for any metric that has
none. A tolerance SHALL be derived from that metric's measured run-to-run spread and SHALL be recorded with what
it was measured from; a tolerance chosen to make a particular run pass has no basis to be reviewed against later.

A metric that does not move between identical runs SHALL NOT inherit a tolerance widened for a different metric.
Noise is a property of how a metric is produced — a live translation, a model judging prose — and not of the suite
that reports it, so a suite-wide number is either too loose for its stable metrics or too tight for its noisy ones.

A metric the baseline does not mention SHALL be reported as new rather than ignored, so that adding a metric cannot
quietly escape the comparison. A baseline that mentions a metric the run did not produce SHALL be reported as
missing.

The baseline SHALL only change when it is explicitly asked to, never as a side effect of running the suites, so that
a regression cannot be absorbed by running them again. The report SHALL carry the comparison, so the screen can show
what moved without recomputing it.

#### Scenario: Threshold failure
- **WHEN** a metric is below its configured threshold
- **THEN** the report marks that suite as failed and the command exits non-zero

#### Scenario: A drop beyond the tolerance fails the run
- **WHEN** a metric is below its baseline by more than the tolerance
- **THEN** the run fails, naming the suite, variant, metric, both values and the drop, and the command exits non-zero

#### Scenario: Noise within the tolerance passes
- **WHEN** a metric is below its baseline by no more than the tolerance
- **THEN** the run passes and the comparison still reports the difference

#### Scenario: A metric with its own tolerance
- **WHEN** a metric has a tolerance configured for it and drops by less than that but more than its suite's
- **THEN** the run passes

#### Scenario: A stable metric beside a noisy one
- **WHEN** a metric with no tolerance of its own drops by more than the default, in a suite whose tolerance was widened for another metric
- **THEN** the run fails

#### Scenario: Nothing configured for a metric
- **WHEN** a metric has no tolerance of its own
- **THEN** its suite's tolerance applies, and the default where the suite has none

#### Scenario: An improvement never fails
- **WHEN** a metric is above its baseline
- **THEN** the run passes and the comparison reports the gain

#### Scenario: A metric with no baseline
- **WHEN** a run produces a metric the baseline does not mention
- **THEN** the comparison reports it as new and the run does not fail on it

#### Scenario: The baseline only moves when asked
- **WHEN** a run's metrics are better than the baseline and no one asked to accept them
- **THEN** the baseline file is unchanged

#### Scenario: Accepting the baseline
- **WHEN** the run is asked to accept its results
- **THEN** the baseline records those metrics, with the run they came from and when

