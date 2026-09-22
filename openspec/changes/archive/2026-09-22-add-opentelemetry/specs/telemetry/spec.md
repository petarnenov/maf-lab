# Spec Delta

## Purpose
What the stack emits about itself and what it may never emit: the traces, metrics and logs every service produces,
how one turn is followed across all of them, and where those signals go.

## ADDED Requirements

### Requirement: Every service emits the three signals over OTLP
Each service of the lab that runs .NET SHALL emit traces, metrics and logs over OTLP to a configured endpoint.
Every signal SHALL carry the service's name and the identity of the instance that produced it — the same instance
name the load balancer already reports — so a number can always be attributed to the replica that produced it.

Telemetry SHALL be configurable and SHALL be off when no endpoint is configured, so the test suite and a local run
need no collector. A collector that is unreachable SHALL NOT fail a request, slow a turn beyond its export timeout,
or fill the logs with repeated failures.

#### Scenario: Signals carry their origin
- **WHEN** a turn runs on one api replica and its retrieval on one MCP replica
- **THEN** the spans and metrics of each carry that service's name and that replica's instance name

#### Scenario: No endpoint configured
- **WHEN** no OTLP endpoint is configured
- **THEN** nothing is exported and the system runs exactly as it did before

#### Scenario: The collector is down
- **WHEN** the collector cannot be reached
- **THEN** turns still answer, and the failure is reported once rather than per export

### Requirement: The framework's own instrumentation is used, not a hand-rolled copy
The agent and every model call SHALL be instrumented through the instrumentation the agent framework and the AI
abstractions already provide, and HTTP, incoming requests and database access through their standard
instrumentation. A span SHALL be written by hand only where no library provides one — the MCP tool call, the
vector store query and the sparse encode.

No measurement SHALL be taken twice: where an instrument already reports a duration or a count, the system SHALL
NOT keep a second hand-rolled aggregate of the same thing.

#### Scenario: A model call
- **WHEN** the agent calls the chat model
- **THEN** the span and the duration and token metrics come from the framework's instrumentation, under the GenAI
  conventions, with nothing about them written by hand

#### Scenario: A retrieval
- **WHEN** the MCP server answers `search_documents`
- **THEN** there is a span for the tool call, one for the vector store query and one for the sparse encode

### Requirement: A turn is one trace from the browser to the vector store
The trace context of a chat run SHALL travel from the browser to the api, from the api to the MCP server and to
every other service a turn reaches, so that the whole turn is one trace. The api SHALL report, to the client that
asked, the identifier of the trace its run belongs to.

#### Scenario: End to end
- **WHEN** a user asks a question that retrieves documentation
- **THEN** one trace holds the browser's run, the api's turn, the model calls, the MCP tool call and the vector
  store query, in their real parent-child order

#### Scenario: The client learns its trace
- **WHEN** a run streams
- **THEN** the client is told the trace identifier of that run

### Requirement: No content in any signal
No prompt, system prompt, question, answer, reasoning, document text, snippet or tool free-text argument SHALL
appear in any span, metric or log — in attributes, names or events. Where the framework's instrumentation can be
asked to record message content, it SHALL be configured not to. Identifiers, names, counts, durations and outcomes
are what telemetry carries.

#### Scenario: A traced turn
- **WHEN** a turn retrieves a document and answers
- **THEN** no span, metric or log holds the question, the answer, the reasoning, the prompt or any snippet

#### Scenario: Sensitive-data capture stays off
- **WHEN** the GenAI instrumentation offers to record prompts and completions
- **THEN** it is configured not to, and turning it on is not a supported configuration of this system

#### Scenario: A tool call's arguments
- **WHEN** a tool is called with a query
- **THEN** the span names the tool and the identifiers, and not the query

### Requirement: What the stack measures about itself
The system SHALL report, as metrics: how many runs and turns are started, finished and failed; how long a turn
takes; how long a model call takes and how many tokens it used, by model; how many tool calls are made, by tool
and outcome; how long retrieval takes, split into its stages; and how many turns end waiting for a person. Each
SHALL be attributable to the instance that served it.

#### Scenario: Tool outcomes
- **WHEN** a tool call fails
- **THEN** it is counted under that tool with a failed outcome, separately from the calls that succeeded

#### Scenario: Token usage
- **WHEN** the provider reports token usage
- **THEN** input and output tokens are recorded against the model that used them

#### Scenario: A paused turn
- **WHEN** a turn ends waiting for an advisor's approval
- **THEN** it is counted as such, and not as a failure
