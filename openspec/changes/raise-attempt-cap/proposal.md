# Proposal

## Why

Five attempts are too few for a file whose tests take a few rounds of compile errors to get right. The run on
`src/Maf.Lab.A2A/RedisTaskStore.cs` read in attempts 1–2, wrote in 3 and first passed in 4, leaving one attempt of
slack. The user asked for 10 attempts per run.

The cap is also written in several places: the contract's `AttemptLimit`, the api's `TestAgent:MaxAttempts` (in code
and again in `appsettings.json`), the agent card, and UI text ("five attempts"). Changing it today means changing all
of them. Like the tool-round cap (DECISIONS §60), it should have one home.

## What Changes

- **A run has up to 10 attempts.** `TestGenRequest.AttemptLimit` becomes 10, and it is the only place the number is
  written. The api's default reads it, `appsettings.json` stops repeating it, and the agent card, the handler's
  comment and the UI say "the attempt cap" or read the number from the server.
- **The run deadline grows to fit.** It goes from 45 min to 2 h, still configurable. Measured runs took 3–4 min per
  attempt at 12 tool rounds. With 40 rounds and 10 attempts, 45 min would cancel runs that are still making progress.
- **The confirmation no longer says "five".** Its first step drops the number, and the model picker, which already
  shows `maxAttempts` from the server, states it.

## Capabilities

### New Capabilities
<!-- None. -->

### Modified Capabilities
- `test-generation-agent`: a task may ask for at most 10 attempts.
- `test-generation-runs`: the run deadline fits the attempt cap, not 5 attempts.
- `model-selection`: the estimate and the limit text count up to the attempt cap (10).
- `coverage-dashboard`: progress reads attempt n of N (the run's cap), not n of 5.

## Impact

- `src/Maf.Lab.TestGen/AgentContracts.cs` (`AttemptLimit = 10`).
- `src/Maf.Lab.Api/Coverage/TestAgentOptions.cs`: `MaxAttempts` defaults to `TestGenRequest.AttemptLimit`, and
  `RunDeadline` becomes 2 h. `appsettings.json` loses `"MaxAttempts": 5`.
- `src/Maf.Lab.TestAgent/TestAgentCard.cs` and the `TestGenerationHandler` comment; `web/src/coverage/RaiseThresholdDialog.tsx`.
- Tests that assume 5 (the invalid-attempts theory, the api models test).
- Cost: an unlimited run can now use twice as many attempts. The per-run budget in the picker is the control for that.

## Documentation impact

- `docs/http-api.md` does not state the attempt count (it names `maxAttempts` as a field), so it does not change.
- The agent card's description, which the api's A2A discovery shows, says "the attempt cap". No doc repeats it.
- README.md, CLAUDE.md, openspec/project.md and .github/copilot-instructions.md do not mention the attempt cap, so
  none of them changes.

Note: `explain-run-outcome` and `add-mocking-library` are complete but not archived, and they modify the same
requirements. The deltas here are written on top of their text, so archive those two first.
