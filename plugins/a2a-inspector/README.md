# a2a-inspector

[A2A Inspector](https://github.com/a2aproject/a2a-inspector) on http://localhost:7172, a developer tool allowed in dev and qa (`make plugin-on NAME=a2a-inspector`, `make plugin-off NAME=a2a-inspector`).
It is linked from the web UI's navigation and published on `127.0.0.1` only.

It opens with the assistant's card URL and a fresh partner token (`acme-portal`); press **Connect**. Change the URL to
`http://localhost:7171/compliance/.well-known/agent-card.json` and the token switches to one for the compliance agent.

It shares the balancer's network, so `localhost:7171` means the lab inside it too — which is why the URL a card
advertises works as is, on macOS and Linux alike.

The tokens are minted from the dev credentials in the compose files — the A2A page asks for a new one each time it
opens (`/lab/token`), the MCP catalog is rewritten with a new one every hour — and are never stored outside the
containers. By hand: `POST /dev/token` for MCP, `POST /a2a/token` or `/compliance/a2a/token` for A2A; a token for one
A2A audience is refused by the other.
