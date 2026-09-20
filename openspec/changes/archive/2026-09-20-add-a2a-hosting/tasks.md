# Tasks

## 1. Partner identity

- [x] 1.1 Add the partner token kind to the dev issuer: subject = partner id, audience = the A2A endpoint, a `scope` claim, and `A2A:Partners` configuration mapping a partner to its allowed firms and scopes; verify tests: a partner token validates at the A2A audience and not at the chat one, a chat token is refused by the A2A validator, and entitlements come from configuration even when the token claims otherwise
- [x] 1.2 A `PartnerPrincipal` that is not a `Principal`: it carries the partner id and its allowed firms, and cannot be used where a user is expected; verify a test that the type system (or an explicit guard) prevents a partner from being passed to the tenant-scoped query path as a user

## 2. The card

- [x] 2.1 Build the public `AgentCard` (name, description, version, both transports, security schemes for OAuth2 client credentials with `a2a.billing.read`, and skills whose descriptions state what each is for **and not for**) and serve it at the well-known path; verify tests: the card deserialises with the SDK's own types, lists both transports, and contains no private skill
- [x] 2.2 Sign the card and document the verification procedure; verify a test that verifies the signature the documented way, and that a tampered card fails verification
- [x] 2.3 Declare and serve the extended card, adding `start_billing_run`, to authenticated callers only; verify tests: the public card never mentions it, the extended card contains it, and an unauthenticated extended-card request is refused

## 3. Task state that outlives the process

- [x] 3.1 `SqliteTaskStore : ITaskStore` over the shared database, with the task and its update history keyed by task id and context id (tables added by the existing additive initializer); verify tests: round trip of a task and its updates, two store instances (standing in for two replicas) seeing the same task, and concurrent updates not losing a transition
- [x] 3.2 Push-notification configuration persisted beside the task (create, get, list, delete); verify tests for each, including that a configuration is scoped to its task

## 4. Serving work

- [x] 4.1 `IAgentHandler` that answers a direct question with a message by running the existing agent (same system prompt, same MCP tools) under the partner principal, with the partner's allowed firms as the tenant scope; verify tests: a run-status question returns a message, the answer is produced by the same tool path, and a question about a firm outside the entitlement is rejected with a fixed reason and no data
- [x] 4.2 The long-running skill as a task: states reported while it works, an artifact (DataPart) at the end drawn from the seeded run, cancellation honoured while working, and the input-required state when a required parameter is missing with resumption under the same task id; verify tests for each of the four, including that the artifact matches what a later fetch of the task returns
- [x] 4.3 Map both transports (`MapA2A` for JSON-RPC, `MapHttpA2A` for HTTP+JSON) behind partner authentication, and make resubscribe deliver the full current task before further updates; verify a test that drops a stream mid-task, resubscribes, and asserts no transition is missing
- [x] 4.4 Deliver one push per transition with the registered token, retried a fixed number of times and recorded on failure without failing the task; verify tests with a local receiver: one delivery per transition, the token present, and a task that still completes when the receiver is unreachable

- [x] 4.5 Translate the wire format both ways so the surface speaks A2A 1.0 rather than the preview SDK's dialect (method names, role and task-state values, part `kind`, unwrapped results, `final` on the last streamed event, push-notification configuration parameters), over both transports and in the push payload; verify tests: a specification-shaped request is answered, a fetched task and a streamed run read as 1.0, and the translation is unit-tested for file, data and text parts

## 5. Audit and the record

- [x] 5.1 Append every inbound A2A request to the audit chain with its own kind (partner, operation, task id, outcome, duration, never message content); verify tests: a started task, a rejected request, no question text anywhere in the record, and the chain still verifying

## 6. Verification and docs

- [x] 6.1 Run the assistant behind the balancer and drive it with a **separate client built only from the card** (`Microsoft.Agents.AI.A2A`: `A2ACardResolver.GetAIAgentAsync`, no shared code) and with a specification-shaped client that knows nothing of any SDK: discover, authenticate, ask a run status, start a run, drop and resubscribe, cancel, resume from input-required, and be rejected for another firm; assert the task is visible through both replicas
- [x] 6.2 Run `make test`, `make lint`, `COMPOSE_PROJECT_NAME=maf-lab-ci make ci-e2e`, and extend `scripts/verify_lb.sh` with the card fetch and one task through the balancer
- [x] 6.3 Update `docs/http-api.md` (the A2A surface and the partner token), README (what a partner can do and that starting a run is simulated) and `DECISIONS.md`: the package choice and why not the Microsoft A2A packages, the pinned preview version, which transports are mapped and that gRPC is not, that `ITaskStore` needed our own implementation, that the card signature is symmetric and what a real deployment would use, and that push is at-least-once with a shared token; verify the sections exist
