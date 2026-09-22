# Spec Delta

## MODIFIED Requirements

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
