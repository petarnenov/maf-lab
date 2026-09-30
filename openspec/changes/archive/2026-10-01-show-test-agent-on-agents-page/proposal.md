# Proposal

## Why

The "Agent to agent" page (`/admin/a2a`) shows what partners asked of the assistant and what the assistant asked of
the compliance reviewer, but says nothing about the third agent in the stack: the test-generation agent the Coverage
page drives. An operator who wants to know whether that agent is up, who it says it is, what a run gets by default
and what it has been doing has to piece it together from Topology, the Coverage page's run picker and individual
files. The agent is internal-network only, so the browser cannot ask it; the api already knows all of it.

## What Changes

- A new read-only admin endpoint, `GET /api/admin/a2a/test-agent`, returns one purpose-built overview of the test
  agent:
  - whether it is configured and reachable right now (answered by fetching its public card on the internal network,
    with a short timeout and a short cache so opening the page cannot become a probe storm), with a short reason when
    it is not and how long the card took;
  - its card: name, description, version, the one skill (id, name, description, tags), the A2A endpoint it
    advertises on the internal network, the protocol version, the scope a caller needs, and its streaming/push
    capabilities;
  - how the api reaches it: the configured base address and the partner id (client id) the api signs in as — never
    the secret;
  - the defaults a run gets: the default model of the allowlist, and every run limit with its bounds and default
    (attempt cap, tool rounds per attempt, test runs per attempt, suspected-bug limit, deadline), and that there is no
    default budget;
  - counts of runs by group (running now, candidates awaiting a decision, accepted, failed, ended otherwise);
  - the most recent runs: file, state, attempt n/N, coverage reached against the target, reason, model, when it last
    changed.
- The Agent to agent page gains a "Test-generation agent" section that shows that overview in the page's existing
  design and theme, with a link to the Coverage page (and each recent run's file opening there). It loads on its own,
  so the section appears whether or not any partner has talked to the system.
- A shared themed progress indicator (`role="progressbar"`, theme tokens, reduced-motion aware) is added to the web
  app and used on this page while the activity and the test-agent overview load or refresh, per `progress-feedback`.
  The Refresh button is disabled while a refresh is in flight.
- The browser never talks to the test agent: every value comes through the api.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `a2a-observability`: an authorised person can see the test-generation agent — its card, whether it is reachable,
  its run defaults and limits, and its runs by state and most recent — through the api, read-only.
- `web-ui`: the A2A screen shows a test-generation agent section, with themed progress while it loads.

## Impact

- Code: `src/Maf.Lab.Api/Coverage` (a probe that reads the agent's card with a timeout and cache, and builds the
  overview), `src/Maf.Lab.Api/Endpoints/A2AAdminEndpoints.cs` (the new route), `web/src/admin/A2AAdminPage.tsx` and a
  new `TestAgentSection`, `web/src/components/Progress` (new), `web/src/api/types.ts`. Tests in `tests/Maf.Lab.Tests`
  and `web/src`.
- API: one new GET route under the existing FIRM_ADMIN `/api/admin/a2a` group. No new tenant parameter: runs belong
  to the repository, not to a firm, exactly as the Coverage API treats them.
- Configuration: two optional `TestAgent` settings (probe timeout, probe cache), with defaults; nothing to set.
- No new make target, CLI tool, package, model, Jev call or load-balancer location. The only UI-started work is
  loading and refreshing the page, which shows the themed indeterminate progress indicator until it ends, then the
  outcome (the data, or an error saying what could not be loaded).

## Documentation impact

- `docs/http-api.md`: the "Agent to agent" table gains the `GET /api/admin/a2a/test-agent` row, and a paragraph says
  what it returns, where each part comes from, that it never contacts the agent from the browser, that the secret is
  never included, and that runs are not firm-scoped.
- `README.md`: the `/admin/a2a` paragraph says the page also shows the test-generation agent (its card, whether it is
  reachable, its run defaults and its runs).
- CLAUDE.md, openspec/project.md, docs/shared-state.md and .github/copilot-instructions.md do not describe what the
  Agent to agent page shows and are unaffected.
