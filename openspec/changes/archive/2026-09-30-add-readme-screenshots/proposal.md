# Proposal

## Why

The README describes a chat with a behind-the-scenes monitor, a live topology, a Jev statistics screen, evals and a
compliance queue, but shows none of them. A reader has to run the whole stack, keys included, before seeing what the
lab looks like. Real screenshots at the top and next to the sections that describe each screen fix that. The UI
changes often, so the screenshots must be cheap to re-take: one command, not a manual session.

The ASCII architecture diagram and several README facts are also behind the code. The diagram has no codebase server,
compliance reviewer, Redis, Jev or observability. Replica counts, the `make verify` check count, the domain eval size,
the `make dev` ports and the target list are stale.

## What Changes

- New `tools/screenshots/`, a small Node package with a Playwright script. It logs in as a seeded dev persona,
  drives the running stack through `http://localhost:7171` into each screen's state, and writes PNGs to
  `docs/screenshots/`, each screen in a light and a dark variant:
  - **chat**: a billing question answered, with the tool card, sources and the monitor's trace in the right pane
  - **code-snippets**: a codebase question, with the right pane on **Code snippets**
  - **topology**: `/topology` with every service healthy
  - **jev**: `/admin/jev`
  - **evals**: `/evals`
  - **compliance**: `/admin/compliance`
- New `make screenshots` target. It installs the package and Chromium if they are missing, requires the stack to be
  up, and re-takes the whole set. `SHOTS=chat,topology` re-takes only the named screens.
- README gains a hero image (the chat) under the introduction, a two-cell gallery (Jev, evals), and the matching
  screenshot next to the sections that describe that screen (topology, code snippets, compliance). Each image uses
  `<picture>` with `prefers-color-scheme`, so GitHub shows the variant that matches the reader's theme. A line in **Quick start**'s
  table names `make screenshots`.
- The ASCII diagram becomes a Mermaid flowchart, which GitHub renders in the reader's theme. It shows every compose
  service and external dependency: the three MCP servers, compliance, Redis, SQLite, Qdrant, Ollama, Ollama Cloud,
  Jev, and the OTel/Prometheus/Jaeger chain. Its edges follow `docs/topology.drawio` and `compose/`.
- README is synced with the code:
  - replica counts and balancer routes;
  - `make verify`'s check count (also in `make help`) and the domain eval size;
  - the `make dev` ports;
  - the Jev warm-up services;
  - the eval and index targets, plus `ci`/`ask`/`eval-accept` rows;
  - links to the change archive, `docs/*.md` and the list of screens.
- `DECISIONS.md` records the new dev-only dependency (`playwright`, pinned) and why it lives outside `web/`.
- No application code, configuration or runtime behaviour changes.

## Capabilities

### New Capabilities
<!-- None. -->

### Modified Capabilities
<!-- None (skip_specs): the screens are specified in openspec/specs/web-ui. The new make target is developer tooling
     like `make ask`, and make-workflow's requirements do not enumerate targets. -->

## Impact

- New: `tools/screenshots/` (package.json, lockfile, script), `docs/screenshots/` (12 PNGs, about 2–4 MB).
- Changed: `Makefile` (one target and the verify help text), `README.md`, `DECISIONS.md`.
- Running `make screenshots` needs the stack up with `OLLAMA_API_KEY` and `JEV_MAF_LAB`. Two chat turns per run
  spend a little Ollama Cloud and Jev credit. It is not part of `make ci`, and no GitHub workflow runs it.
