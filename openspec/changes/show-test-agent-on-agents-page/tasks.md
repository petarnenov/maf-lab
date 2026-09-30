# Tasks

## 1. Api

- [ ] 1.1 Add `TestAgent:ProbeTimeout` (2 s) and `TestAgent:ProbeCacheFor` (10 s) to `Coverage.TestAgentOptions` and a `TestAgentProbe` that fetches the agent's public card anonymously within the timeout, parses it into a card DTO, maps failures to a short reason, and caches the result; register it in `Program.cs`; verify `make lint-dotnet` builds with no warnings
- [ ] 1.2 Add `GET /api/admin/a2a/test-agent` to `A2AAdminEndpoints` returning the overview DTO (configured, status, card, connection without the secret, default model, `RunLimitsDto` limits, no default budget, run counts by group, the ten most recent runs); verify with xUnit tests in `tests/Maf.Lab.Tests/TestAgentOverviewApiTests.cs`: reachable agent shows its card and scope; unreachable agent still returns defaults and runs with a reason; unconfigured says so; seeded runs give the right counts and newest-first recent runs; the client secret is not in the body; an advisor gets `403`; a second request within the cache window does not fetch the card again

## 2. Web

- [ ] 2.1 Add a themed `Progress` component in `web/src/components` (`role="progressbar"`, accessible label, determinate when given value/max, theme tokens only, no animation under reduced motion); verify with a Vitest case for both forms
- [ ] 2.2 Add the `TestAgentOverview` types to `web/src/api/types.ts` and a `TestAgentSection` in `web/src/admin` that loads `/api/admin/a2a/test-agent` on its own and shows status, card, defaults, counts with a link to `/coverage`, and recent runs linking to `/coverage?file=<path>`; verify with Vitest cases: reachable, unreachable, not configured, load failure, progress while loading
- [ ] 2.3 Render the section in `A2AAdminPage` whatever the activity holds, use `Progress` while either query loads or refreshes, and disable Refresh while fetching; verify the existing `A2AAdminPage` tests still pass and a new case shows the section beside the empty state

## 3. Verification

- [ ] 3.1 Run `make lint`, `dotnet test --project tests/Maf.Lab.Tests` and `make test-web`; all pass
- [ ] 3.2 Live check: open `http://localhost:7171/admin/a2a` as a FIRM_ADMIN on the running stack, see the section report the test agent reachable with its card and runs, in both themes

## 4. Documentation

- [ ] 4.1 Update `docs/http-api.md` (the `GET /api/admin/a2a/test-agent` row and a paragraph on what it returns and where each part comes from) and the `/admin/a2a` paragraph of `README.md`, then run `make docs` and `make docs-check`; both succeed
