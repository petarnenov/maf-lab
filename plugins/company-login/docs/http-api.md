# Company sign-in configuration

| Method | Path | Body | Response |
|---|---|---|---|
| GET | `/api/identity/configuration` | — | Public OIDC `{ authority, clientId, responseType, scope, callbackPath, signedOutPath }`; 404 without company Authority or without the plugin |

Only the public web client ID and trusted issuer settings are returned. No client secret, token or user claim is
part of this response. The issuer URI is the API's validated company authority. `responseType` is `code`; the
client must use PKCE S256. Browser redirect URIs come from the app origin and these fixed callback paths and must
match the reviewed realm export's exact redirect URI list.
