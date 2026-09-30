# Design

## Context

See proposal.md. The attempt cap today:
- `TestGenRequest.AttemptLimit = 5`: the agent rejects a task that asks for more.
- `TestAgentOptions.MaxAttempts = 5`, overridden again by `TestAgent:MaxAttempts: 5` in the api's
  `appsettings.json`. This is what the api puts in each request and returns from `/api/coverage/models`.
- Text: the agent card ("at most five attempts"), a code comment, and the raise dialog ("in up to five attempts").

The web reads the number from the server (`maxAttempts` on the models response, `run.maxAttempts` on a run),
except that one dialog sentence.

## Decisions

### D1. One number, in the contract
`TestGenRequest.AttemptLimit` is the single value. The api's `TestAgentOptions.MaxAttempts` defaults to it, and
`appsettings.json` no longer sets it. The option stays so an operator can run fewer attempts, and the agent still
rejects more than the limit.
- *Alternative: the api's option as the source, and the agent trusting whatever it is sent.* The agent's guard
  against an oversized task is part of its contract (test-generation-agent: Task input). It needs the limit on its
  own side, and the shared contract is the one place both sides see. Rejected.

### D2. Deadline 2 h, configurable
`RunDeadline` defaults to 2 h. It is not derived from the cap: attempt time depends on the model, the file and the
runner, and a formula would pretend to a precision it lacks. The comment says what it has to fit.

### D3. The UI says the number only where the server gives it
The confirmation's first step drops "in up to five attempts". The picker already says "Estimated cost for up to
{maxAttempts} attempts" and "after at most {maxAttempts} attempts".

## Risks / Trade-offs

- [An unlimited run can now cost about twice as much] → The picker shows the estimate and the "unlimited" note. The
  budget fields cap a run.
- [The estimate scales with the cap, and it already undercounts about 6× per attempt] → This is unchanged here and
  noted for a separate fix of `AttemptEstimate`.
