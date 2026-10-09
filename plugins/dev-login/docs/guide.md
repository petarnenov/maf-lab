# Development sign-in

Install this plugin only in dev/qa. It provides the persona selector and `/dev/users` and `/dev/token`.
`make dev-token PERSONA=adam AUDIENCE=api` prints one token. The operator can request an organization explicitly:
`make dev-token PERSONA=operator TENANT=firm-b AUDIENCE=api`. That token retains the operator identity and role.
Other predefined personas keep their own tenant and role. The requested plugin audience must be installed and in
use for that tenant; a disabled plugin receives no token. Each MCP Inspector entry requests its own audience.

The plugin contributes its controls through the shell's generic authControls registry. Removing it removes the
issuer and selector. Product environments refuse its manifest; production uses the configured external IdP.
