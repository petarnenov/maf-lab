# mcp-inspector

[MCP Inspector](https://github.com/modelcontextprotocol/inspector) on http://localhost:7173, a developer tool allowed in dev and qa (`make plugin-on NAME=mcp-inspector`, `make plugin-off NAME=mcp-inspector`).
It is linked from the web UI's navigation and published on `127.0.0.1` only.

It opens with the lab's MCP servers listed — the built-in domains' ("maf-lab billing", "maf-lab portfolio",
"maf-lab code") and every installed MCP plugin's, re-read from the installed set every 30 s — each with a dev token
for `adam` (USER, firm-a, set by `LAB_USER_ID`/`LAB_TENANT_ID`/`LAB_ROLE`); switch one on. It keeps the servers you add
only until its container restarts.

It shares the balancer's network, so `localhost:7171` means the lab inside it too.

The tokens are minted from the dev credentials in the compose files — the A2A page asks for a new one each time it
opens (`/lab/token`), the MCP catalog is rewritten with a new one every hour — and are never stored outside the
containers. By hand: `POST /dev/token` for MCP, `POST /a2a/token` or `/compliance/a2a/token` for A2A; a token for one
A2A audience is refused by the other.
