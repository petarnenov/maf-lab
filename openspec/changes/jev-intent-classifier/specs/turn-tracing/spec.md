# Spec Delta

## MODIFIED Requirements

### Requirement: Complete turn trace
For every chat turn the system SHALL record an ordered trace of timestamped events. The trace MUST cover:
- turn start: principal, conversation, turn id, api replica, question;
- intent classification: the intent, whether retrieval was forced, the judge's raw answer, its confidence, the
  probability it gave each intent, how long the classification took, and, when no intent was accepted, why;
- the history window: included messages with roles, text and token counts, the token budget, and how many older
  messages were left out;
- the system prompt version and full text, and each offered tool with its description and input schema;
- every model call: iteration, full request messages and options (tool mode, temperature, reasoning setting), response
  text, requested tool calls, finish reason, input and output token usage when the provider reports it, latency,
  model and endpoint host;
- any tool call issued on the model's behalf to force retrieval;
- every tool call: full arguments, raw MCP result, error flag, latency and serving MCP replica;
- the exact data envelope the model received;
- the model's reasoning as ordered chunks (`reasoning.delta`, each with its character offset), coalesced from the
  reasoning the model streamed; concatenating them yields exactly what it reasoned. A model that reasons in
  several stretches across a turn SHALL have each recorded in the order it arrived;
- the streamed answer text as ordered chunks (`answer.delta`, each with its character offset). Chunks are coalesced
  from the model's text deltas, and concatenating them yields exactly the answer sent to the client;
- audit rows written, sources, production signals, memory rows stored, and turn end with total duration and any error.

#### Scenario: Procedural turn trace
- **WHEN** a user asks "What is the procedure when a fee schedule is missing?"
- **THEN** the trace contains, in order: turn start, intent (procedural, forced, with the judge's confidence), history, prompt and tools, the forced search call, the MCP result with retrieval diagnostics, the envelope, at least one model call with its response, sources, signals, and turn end

#### Scenario: Intent decided by the model
- **WHEN** the decision judge classifies the question with enough confidence
- **THEN** the intent event records its duration, raw answer, confidence and the probability it gave each intent

#### Scenario: Intent not accepted
- **WHEN** the decision judge answers below the confidence threshold, fails, or times out
- **THEN** the intent event records no recognised intent and why the judge's answer was not accepted

#### Scenario: Unknown tool attempt
- **WHEN** the model calls a tool that does not exist
- **THEN** the trace records the attempt, the refusal returned to the model, and the audit row

#### Scenario: Answer reconstructable from the trace
- **WHEN** a turn streams an answer
- **THEN** the `answer.delta` events have contiguous offsets starting at 0, and their texts concatenated equal the full answer

#### Scenario: Reasoning reconstructable from the trace
- **WHEN** a turn's model reasons before it answers
- **THEN** the `reasoning.delta` events have contiguous offsets starting at 0, their texts concatenated equal
  everything the model reasoned, and they are recorded before the model response that followed them

#### Scenario: A turn whose model does not reason
- **WHEN** a model answers without reasoning
- **THEN** the trace holds no `reasoning.delta` event and the rest of the trace is unchanged

#### Scenario: Reasoning stays out of the logs
- **WHEN** a turn's model reasons
- **THEN** no log line contains any of that reasoning
