# system-topology Specification

## Purpose
Reports the running shape of the stack — every service, its replicas, its health and the few numbers worth seeing —
and keeps that report in step with the drawn diagram that the UI renders it on.

## Requirements

### Requirement: Topology report
The system SHALL expose, to any signed-in user, a report of the stack it is running in. The report SHALL contain one
entry per service the lab is made of: the load balancer, the api, the MCP retrieval server, the vector store, the
model provider used for chat, the model provider used for embeddings, the web app, the telemetry collector, the
metrics store and the trace store. Each entry SHALL carry a
stable id, a display name, a health state of `healthy`, `degraded` or `unreachable`, the instances found for it (each with its
name and its own health), and the time the state was determined. The report SHALL also carry the
edges between services, so the picture of what talks to what comes from the system and not only from the drawing.

The report MUST contain infrastructure facts only: no tenant data, no message content, no credentials — in
particular, a configured API key SHALL never be reported, only whether one is configured.

#### Scenario: Every service is reported
- **WHEN** a signed-in user requests the topology
- **THEN** the report contains an entry for each service and the edges between them

#### Scenario: The telemetry services are part of the stack
- **WHEN** the topology is requested
- **THEN** the collector, the metrics store and the trace store are reported like any other service, with the
  edges that carry telemetry to them

#### Scenario: Replicas are named
- **WHEN** api and the MCP server each run two replicas
- **THEN** each entry lists both instance names, whether or not those replicas have ever served a request

#### Scenario: One replica of a service is down
- **WHEN** one of the two api replicas does not answer
- **THEN** api is reported `degraded`, the answering replica is listed healthy and the silent one is not listed healthy

#### Scenario: Replicas cannot be discovered
- **WHEN** the system runs as a single process, outside the container network
- **THEN** the report lists the one instance it can speak for, and says discovery was unavailable rather than failing

#### Scenario: Not signed in
- **WHEN** the topology is requested without a valid token
- **THEN** the request is rejected as unauthorized

#### Scenario: No secrets
- **WHEN** the chat provider is configured with an API key
- **THEN** the report states that a key is configured and never contains the key

### Requirement: Health is measured, not assumed
Each service's health SHALL be determined by contacting it, not by assuming it is up because the api is. Every probe
SHALL have a short timeout and the probes SHALL run concurrently, so one unreachable service cannot delay the report
beyond that timeout. A service that answers is `healthy`; a service that answers but is missing something it needs to
work is `degraded`; a service that does not answer within the timeout is `unreachable`. The report SHALL be produced
even when every probe fails, and SHALL say for each service why it is not healthy.

A report SHALL be reusable for a short, stated period, so that opening the page repeatedly does not put load on the
stack.

#### Scenario: A stopped service
- **WHEN** the vector store is stopped and a user requests the topology
- **THEN** the vector store is reported `unreachable` with a reason, every other service keeps its own state, and the
  report still returns

#### Scenario: A slow service does not block the report
- **WHEN** one service does not answer
- **THEN** the report is still returned within the probe timeout

#### Scenario: Degraded rather than down
- **WHEN** the vector store answers but the expected collection is missing
- **THEN** it is reported `degraded` with that reason, not `healthy` and not `unreachable`

### Requirement: Facts worth seeing
Where a service can cheaply report something that explains the lab's behaviour, the report SHALL include it: the
number of indexed chunks and the collection name for the vector store, the chat and embedding model names and
whether chat runs against a remote provider, the number of tools offered by the MCP server, and each service's
version where it reports one.

#### Scenario: Index size
- **WHEN** the corpus has been indexed
- **THEN** the vector store entry reports the collection name and the number of chunks in it

#### Scenario: Models
- **WHEN** chat runs on a remote provider and embeddings on a local one
- **THEN** the report names both models and shows that chat is remote

### Requirement: The diagram is the drawing, the report is the truth
The repository SHALL contain the topology diagram in an editable, text-diffable draw.io file, and that file SHALL be
the only place the layout, labels and shapes of the picture are defined. Every node in the diagram SHALL correspond
to a service in the report and every service in the report SHALL appear in the diagram, matched by the stable id. A
mismatch SHALL fail the test suite, so adding a service to the stack cannot leave the picture behind.

#### Scenario: A service is added without being drawn
- **WHEN** the report gains a service that the diagram does not contain
- **THEN** the test suite fails, naming the missing node

#### Scenario: The diagram stays readable in version control
- **WHEN** the diagram file is committed
- **THEN** its content is uncompressed XML, so a change to it is reviewable as a diff
