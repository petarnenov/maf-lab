# Spec Delta

## ADDED Requirements

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
