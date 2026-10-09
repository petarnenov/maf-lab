# Live topology

Open `/topology` while this developer plugin is installed. It overlays live health, replicas and display facts on
its draw.io diagram, embedded from `files/topology.drawio`. Edit it as uncompressed mxGraph XML. The drawing is the
single source of edges; the report contains vertices and their state. The diagram is served with no-cache.

Core nodes report the balancer, web, API, shared state, local embedding roles and configured chat provider. Installed
plugins declare their own services in `[topology]`, with additional `[[topology.nodes]]` entries when needed. Domain
servers list tools over the official MCP client with the current user's bearer token. Requests run concurrently,
with a two-second budget and a five-second cache per token. Esc aborts the read; the last report stays visible.

Vector facts use the shared collection adapter. Graph connectivity exposes no driver message; authentication is
reported as credentials refused and other failures by type. The paid chat provider is described from configuration
and is never pinged. No credentials, model content or Docker socket are read by this screen.
