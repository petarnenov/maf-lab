# Proposal

## Why

The topology is currently an ASCII drawing in the README, and it is already out of date the moment anything moves: it
cannot show that api runs two replicas, that one of them is unhealthy, that the index holds 3330 chunks, or that chat
goes to Ollama Cloud while embeddings stay local. For a lab whose point is to *watch* a distributed RAG system work,
the one picture of the system is the one thing you cannot look at while it runs.

## What Changes

- A **draw.io diagram in the repository** (`docs/topology.drawio`) becomes the source of truth for the picture:
  which boxes exist, what they are called, how they are laid out and which edges connect them. It is edited in
  draw.io like any other diagram.
- A new **`/api/topology`** endpoint reports the live state of every service the stack is made of: reachability,
  replica identities, versions, and a few facts worth seeing (index size, configured models, chat provider).
- A new **`/topology` page** in the web app renders the diagram with that state on top: healthy, degraded or
  unreachable per node, replica count, and the numbers next to the box they belong to. It refreshes while open and
  says when the picture was last refreshed.
- The diagram and the report must agree: every node the report knows is in the diagram and vice versa, enforced by a
  test rather than by discipline, so a service added to compose cannot quietly vanish from the picture.
- The README's ASCII drawing points at the page instead of trying to stay accurate.

## Capabilities

### New Capabilities

- `system-topology`: what the system reports about its own running shape — the nodes and edges it is made of, how
  each one's health is determined, what an unreachable dependency does to the report, and the diagram it must stay
  in step with.

### Modified Capabilities

- `web-ui`: a new requirement for the `/topology` screen — the diagram as drawn, live state on top, refresh
  behaviour, and what it shows when the report cannot be loaded.

## Impact

- `docs/topology.drawio` (new, uncompressed mxGraph XML so it can be read and diffed), served to the web app.
- `src/Maf.Lab.Api/Endpoints/TopologyEndpoints.cs` (new) and a probe service that checks each dependency with a short
  timeout, in parallel, cached for a few seconds so the page cannot hammer the stack.
- No schema change: replicas are discovered through Docker DNS and asked their existing anonymous `/health`, which
  already answers with the container's instance name.
- `web/src/topology/*` (new: parse the diagram, render it as SVG, overlay state), one entry in the nav `LINKS`,
  one route in `App.tsx`, DTOs in `web/src/api/types.ts`. No new npm dependency: the SVG is rendered from the
  diagram's own geometry.
- `docs/http-api.md`, `README.md`, `DECISIONS.md`.
- No change to tenancy, retrieval, the query path or the chat contract. The endpoint reports infrastructure only —
  never tenant data, never message content.
