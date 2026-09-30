# Proposal

## Why

A test-generation run does not survive a restart of the test agent. The agent runs a task inside the process that
received it; when that process stops (`make` after `make down`, a crash, a redeploy), the task stays `working` in the
shared store and nothing runs it again. The api keeps following it, and the Coverage page shows "Working · 1/10 ·
generating" with the elapsed time climbing until the run's deadline, hours later. The api side already survives its
own restart (another replica takes over the follow lease); the agent side has no recovery at all.

## What Changes

- The agent keeps a checkpoint of each task in the shared store: the request, and after the baseline and after every
  finished attempt, what the next attempt needs (the baseline, the attempts so far, the best result and its diff, the
  tests written so far, the feedback, usage, and the activity sequence).
- While it runs a task, the agent holds a lease on it in the shared store and renews it. A lease that is not renewed
  lapses.
- On start, and periodically after, the agent takes over every unfinished task whose lease has lapsed and resumes it
  from its checkpoint: the interrupted attempt runs again from the end of the last finished one, activity numbering
  continues, and usage continues from what was recorded.
- A resumed task records a new `resumed` activity entry (the attempt it resumes at). The api relays it to the browser
  as `CUSTOM maf-lab/testgen-resumed`, and the activity panel shows a notice that the agent restarted and resumed.
- A task that cannot be resumed (no checkpoint, or its workspace cannot be rebuilt) ends `failed` with the new reason
  `interrupted` instead of hanging; the api maps it to the run's reason.
- The checkpoint and lease are removed when the task ends.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `test-generation-agent`: a task survives a restart of the agent (checkpoint, lease, takeover, resume, `interrupted`);
  activity gains the `resumed` kind.
- `test-generation-runs`: the browser stream carries the `resumed` entry as `CUSTOM maf-lab/testgen-resumed`.

## Impact

- Code: `src/Maf.Lab.TestAgent` (handler split into start and resume, a checkpoint/lease store, a recovery background
  service, reporter starting sequence), `src/Maf.Lab.TestGen/AgentContracts.cs` (`resumed` type, `interrupted`
  failure), `src/Maf.Lab.Api/Coverage` (projection of `resumed`, failure code mapping), `web/src/coverage` (notice in
  the timeline). Tests in `tests/Maf.Lab.Tests` and `web/src`.
- Redis: two new keys per running task under the agent's keyspace (checkpoint, lease), removed at the end, expiring
  with the task's retention.
- No new endpoint, route, make target, package, model or Jev call. No new UI action: the resume happens on its own and
  its progress is the run's existing themed activity panel and progress state.

## Documentation impact

- `docs/shared-state.md`: the "Where each thing lives" table gains the test agent's tasks, checkpoints and leases in
  Redis, and a short section says how a task survives an agent restart.
- `docs/http-api.md`: the run event stream's row lists its custom events; it gains
  `CUSTOM maf-lab/testgen-resumed` (`{attempt}`).
- README.md, CLAUDE.md, openspec/project.md and .github/copilot-instructions.md do not describe run recovery and are
  unaffected.
