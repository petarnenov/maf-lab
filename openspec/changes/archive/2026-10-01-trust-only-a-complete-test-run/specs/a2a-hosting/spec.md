## MODIFIED Requirements

### Requirement: The wire format is the specification's, not the SDK's
Requests and responses on the A2A surface SHALL use the A2A 1.0 wire format, whatever dialect the underlying SDK
speaks: the specified method names, the specified role and task-state values, parts that carry their `kind`, and
results that are the object itself rather than a wrapper around it. A client written against the specification
SHALL interoperate without knowing which library serves it.

Every divergence between the SDK and the specification SHALL be recorded where a reader can find it, together with
what is done about it.

Translating an answer SHALL NOT change what a handler may do with the response body. A flush of a document (a
non-streaming answer), synchronous or asynchronous, SHALL be accepted without failing and without ending the
response; the document SHALL reach the caller complete.

#### Scenario: A specification-conformant request is understood
- **WHEN** a client sends `message/send` with `"role": "user"` and a part whose `kind` is `text`
- **THEN** the request is accepted and answered

#### Scenario: A specification-conformant response is returned
- **WHEN** a task is fetched
- **THEN** its state reads `completed`, its messages read `agent`, its parts carry their `kind`, and the result is the task itself

#### Scenario: Streamed events are specified events
- **WHEN** a caller streams a run
- **THEN** each event carries its `kind`, the last one is marked final, and no earlier one is

#### Scenario: The divergences are written down
- **WHEN** a reader opens the decision record
- **THEN** it names each place the preview SDK departs from 1.0 and what the service does about it

#### Scenario: A document is flushed mid-write
- **WHEN** a handler writes part of a non-streaming answer, flushes it synchronously or asynchronously, and writes
  the rest
- **THEN** no error is raised and the caller receives the whole document as written
