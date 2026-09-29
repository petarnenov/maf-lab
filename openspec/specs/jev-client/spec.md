# jev-client Specification

## Purpose
How every service reaches TypeSafe's Jev at the transport level: one reused, kept-alive connection per process, a
warm-up at start-up, bounded retries inside the caller's budget, and structured logging of every attempt.

## Requirements

### Requirement: One kept-alive connection for every Jev request
Within one process, every request to Jev — intent classification, content screening, the relevance judgment and the
answer check — SHALL be sent through the same long-lived HTTP client, whose connections are pooled and kept alive
between requests. Callers SHALL NOT create a client of their own per request. Pooled connections SHALL be recycled
after a configured lifetime, so that a change of the endpoint's address is picked up without a restart.

#### Scenario: Consecutive requests share a client
- **WHEN** a process sends two Jev requests, from the same caller or from two different callers
- **THEN** both are sent through the same client instance, and the second reuses an open connection when the first left one

#### Scenario: Every caller uses the shared client
- **WHEN** the intent classifier, the content guard, the relevance judge and the answer check each send a request
- **THEN** all four go through the one shared client, and each still carries the bearer credential and nothing else changes on the wire

### Requirement: Warm-up at start-up
Each service that can call Jev SHALL send one warm-up request to Jev after it starts, so that the first real request
finds an established connection. The warm-up SHALL run in the background: it MUST NOT delay the service becoming ready
and MUST NOT fail start-up, whatever its outcome. It SHALL carry fixed text only — no user content — and SHALL be bounded
by a timeout. It SHALL be skipped when the Jev key is missing or when warm-up is disabled by configuration, and its
outcome (status code or failure, and duration) SHALL be logged once. A warm-up SHALL NOT be counted as a turn's Jev call
in the Jev statistics.

#### Scenario: Warm-up on start
- **WHEN** a service starts with the Jev key set and warm-up enabled
- **THEN** exactly one warm-up request is sent to Jev, and one log line records its status code and duration

#### Scenario: Jev unreachable at start-up
- **WHEN** the warm-up request fails or times out
- **THEN** the service still starts and serves requests, and the failure is logged once without a stack trace

#### Scenario: No key
- **WHEN** a service starts without `JEV_MAF_LAB`
- **THEN** no warm-up request is sent

#### Scenario: Warm-up disabled
- **WHEN** warm-up is disabled by configuration
- **THEN** no warm-up request is sent

### Requirement: Bounded retries inside the caller's budget
A Jev request that fails transiently — a transport error, or a 408, 429 or 5xx status (529 included) — SHALL be retried
up to a configured number of times, after a short delay. Any other status (for example 400, 401, 403 or 422) SHALL NOT
be retried. Retries SHALL happen inside the caller's existing budget: the caller's wait SHALL remain bounded by its
timeout, and a retry SHALL NOT start once the budget is spent. When every attempt fails, the caller SHALL receive the
last failure, with the same consequences it has today. The number of retries SHALL be configuration, and zero SHALL
disable retrying.

#### Scenario: Transient failure then success
- **WHEN** Jev answers 503 and then, on the retry, 200
- **THEN** the caller receives the 200 answer

#### Scenario: Non-transient status
- **WHEN** Jev answers 401 or 422
- **THEN** the request is sent once and not retried

#### Scenario: Retries exhausted
- **WHEN** Jev answers 529 to every attempt
- **THEN** the caller receives a rejection carrying status 529 after one attempt plus the configured number of retries

#### Scenario: Budget spent
- **WHEN** the caller's timeout elapses while a retry is pending
- **THEN** no further attempt is sent and the caller's wait ends at its timeout

### Requirement: Every attempt logged with its status code
Every attempt of a Jev request SHALL produce one structured log line carrying the attempt number, the HTTP status code
— or, when there is no response, the transport error's type — and the attempt's duration. A retry SHALL additionally be
logged with the reason it is retried and the delay before the next attempt. A successful attempt SHALL be logged at
information level, a failed attempt or a retry at warning level. These log lines MUST NOT contain the request or
response body, the user's question, any passage text or the Jev key.

#### Scenario: Status code logged
- **WHEN** a Jev request is answered 200 on the first attempt
- **THEN** one log line records attempt 1, status 200 and its duration

#### Scenario: Retry logged
- **WHEN** a Jev request is answered 429 and then 200
- **THEN** the log records attempt 1 with status 429, a retry with reason 429 and its delay, and attempt 2 with status 200

#### Scenario: Transport failure logged
- **WHEN** a Jev attempt fails without a response
- **THEN** the log line records the error's type instead of a status code, and no stack trace or host name reaches any model-facing text

#### Scenario: No content in the log
- **WHEN** any Jev attempt is logged
- **THEN** the log line contains neither the key value nor any text from the request or response body

### Requirement: A circuit breaker in front of every Jev request
Within one process, every request to Jev SHALL pass one shared circuit breaker. This covers:
- intent classification;
- prompt, content and partner screening;
- the relevance judgment;
- the answer check;
- the warm-up.

The breaker SHALL have three states: closed, open and half-open.

- **Closed:**
  - Requests are sent as today.
  - The breaker counts consecutive failed requests.
  - A request answered successfully SHALL reset the count.
  - When the count reaches the configured failure threshold, the breaker SHALL open.
- **Open:**
  - No request SHALL be sent to Jev.
  - Every call SHALL return at once, with no answer and the failure reason `circuit open`, and SHALL record that
    nothing was sent.
  - The open state SHALL last for the configured open period.
- **Half-open:**
  - After the open period, the next call SHALL be sent to Jev as a probe, within that caller's own budget.
  - Other calls made while the probe is pending SHALL be skipped as in the open state.
  - If the probe is answered successfully, the breaker SHALL close.
  - If the probe fails, the breaker SHALL open again for the open period.

Only these outcomes SHALL count as failures:
- the caller's budget ran out (a timeout);
- a transport error;
- a final status of 408, 429 or 5xx after the client's retries.

These outcomes SHALL neither count as failures nor reset the count:
- a missing key;
- the caller cancelling its own request;
- any other rejection status (for example 400, 401, 403 or 422).

The breaker MUST NOT change the budget, the retries or the wire shape of a request that is sent.

A threshold of zero SHALL disable the breaker, and every request SHALL then be sent as before. Each process SHALL hold
its own breaker. Processes SHALL NOT share breaker state.

Every call site SHALL treat `circuit open` exactly as it treats any other outcome in which Jev did not answer. The same
fail-open or fail-closed result SHALL apply, and the reason SHALL be recorded where the site records its failure reason
today.

#### Scenario: Consecutive timeouts open the circuit
- **WHEN** the failure threshold is 3 and three Jev requests in a row time out
- **THEN** the next call returns immediately with reason `circuit open`, and no request reaches Jev

#### Scenario: A success resets the count
- **WHEN** the failure threshold is 3, and two requests fail, one succeeds, and two more fail
- **THEN** the circuit stays closed and the next call is sent to Jev

#### Scenario: Probe after the open period succeeds
- **WHEN** the circuit is open, the open period has elapsed, and the next request is answered successfully
- **THEN** the circuit closes, and the calls that follow are sent to Jev

#### Scenario: Probe fails
- **WHEN** the probe request after the open period times out
- **THEN** the circuit opens again for the open period, and calls return `circuit open`

#### Scenario: One probe at a time
- **WHEN** the open period has elapsed and two calls arrive while the first one's probe is still pending
- **THEN** only the first is sent to Jev, and the second returns `circuit open`

#### Scenario: Non-transient rejection does not open the circuit
- **WHEN** Jev answers 401 to more requests in a row than the failure threshold
- **THEN** the circuit stays closed, and every request is still sent and still fails with its status

#### Scenario: No key
- **WHEN** a process runs without `JEV_MAF_LAB`
- **THEN** every call fails with reason `no key` as today, and the breaker's state does not change

#### Scenario: Call sites keep their unavailable behaviour
- **WHEN** the circuit is open during a turn that screens a prompt, a tool result and a reviewer's words, judges a
  search and checks the answer
- **THEN** the prompt, the tool result and the search proceed unscreened or ungated, the reviewer's words are withheld,
  and the answer is left unchecked, each with reason `circuit open`

#### Scenario: Breaker disabled
- **WHEN** the failure threshold is configured as 0 and Jev times out on every request
- **THEN** every call is sent to Jev and waits its own budget, as before this change

#### Scenario: Breakers are per process
- **WHEN** the api's circuit is open
- **THEN** each retrieval replica still sends its own requests until its own breaker opens

### Requirement: Circuit state changes are logged
Each change of the breaker's state SHALL be logged once, with structured fields only.
- **Opening** SHALL be logged at warning level. The line SHALL carry the number of consecutive failures, the last
  failure's reason and the open period.
- **The start of a probe** SHALL be logged at information level.
- **Closing** SHALL be logged at information level. The line SHALL carry how long the circuit was open and how many
  calls it skipped.

A call skipped while the circuit is open SHALL NOT produce a log line of its own. These log lines MUST NOT contain the
key, a request or response body, or any question, passage or answer text.

#### Scenario: Open and close logged once
- **WHEN** the circuit opens, skips 40 calls, and then closes after a successful probe
- **THEN** the log holds one warning for the opening, one line for the probe and one for the closing, carrying 40
  skipped calls, and no line for each skipped call

#### Scenario: No content in breaker logs
- **WHEN** any breaker state change is logged
- **THEN** the line contains neither the key value nor any text from a request or response
