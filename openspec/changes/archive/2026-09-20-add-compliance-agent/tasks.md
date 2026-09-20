# Tasks

## 1. One place for the shared A2A code

- [x] 1.1 Create `src/Maf.Lab.A2A` and move `PartnerIdentity`, `PartnerAuthentication`, `AgentCardFactory`, `SpecWire`, `SpecWireMiddleware` and `A2ARequestHandlerWithExtras` into it, leaving `BillingAgentHandler`, `AssistantBridge`, `SqliteTaskStore` and `PushNotificationDispatcher` in the API; verify by the existing suite passing unchanged (`make test`) and `make lint` staying clean — no behaviour may change in this task
- [x] 1.2 Make the card factory build a card from options (agent name, description, version, base address, skills, the private skill's id) instead of hard-coding the billing agent's; verify tests: the billing card is byte-for-byte what it was before the move, and a second set of options produces a different card with its own skills

## 2. The reviewer

- [x] 2.1 `src/Maf.Lab.ComplianceAgent`: an ASP.NET Core service with its own Dockerfile, serving its agent card at the well-known path, its token endpoint and both A2A transports through the shared code, with its own audience and partner registry; verify tests: the card lists exactly one skill whose description says it is simulated, an anonymous call is refused, and a token minted for the billing agent's audience is refused
- [x] 2.2 The review itself: submitted → working with progress → completed with a structured verdict (decision, reason, adjustment id, `simulated: true`), with the duration configurable; verify tests: the states arrive in order, the artifact carries every field, and a configured short duration completes accordingly
- [x] 2.3 Refusal and irrelevance: an adjustment beyond the configured threshold is refused with a reason naming it, and a request that is not about a fee adjustment is answered as out of scope with no verdict; verify a test for each
- [x] 2.4 The question back: a configurable rate at which a review stops in `input-required` asking for the advisor's justification, resuming to a verdict when it is supplied under the same task and never asking twice; verify tests with the rate forced to always and to never

## 3. Consulting it

- [x] 3.1 A `ComplianceConsultant` in the API that discovers the reviewer from its card, authenticates with the assistant's own client credentials, and returns `ConsultationResult` — verdict, question, timed out, unreachable, failed; verify tests for each of the five against a stubbed reviewer, including that the user's token never leaves this system
- [x] 3.2 Card discovery cached for a configured interval and dropped when a consultation fails at the transport level; verify tests: a second consultation fetches no card, and a transport failure causes the next one to fetch again
- [x] 3.3 A deadline that ends a consultation without ending the review: the result carries the task id so the answer can be collected later; verify a test that a review longer than the deadline reports `TimedOut` with the task id, and that the review still completes on the reviewer
- [x] 3.4 Audit every consultation through the existing chain with its own kind (agent, operation, task id, outcome, duration, never content); verify tests: a verdict, a timeout and an unreachable agent each leave a record, none contains the adjustment's text, and the chain still verifies

## 4. In the stack

- [x] 4.1 Compose service for the compliance agent with two replicas, balancer routes for its card and its A2A path with path-hash affinity, and the api configured with its base address and credentials; verify `make` brings it up healthy and its card is fetchable through port 7171
- [x] 4.2 Add the reviewer to the topology report and the draw.io diagram, probed like every other service; verify tests: the report contains the node, and it reads unreachable when the service is stopped
- [x] 4.3 Extend `scripts/verify_lb.sh`: the reviewer's card through the balancer, more than one compliance replica answering, and one consultation end to end; verify `make verify` passes with the new checks

## 5. Verification and docs

- [x] 5.1 Live: run the stack, consult the reviewer for a verdict, for a question, and with the reviewer stopped, and confirm each outcome and its audit record; record what the stack showed
- [x] 5.2 Run `make test`, `make lint`, `COMPOSE_PROJECT_NAME=maf-lab-ci make ci-e2e`
- [x] 5.3 Docs: `docs/http-api.md` (the reviewer's surface and how the assistant authenticates to it), README (what the second agent is and that its verdict is simulated), `DECISIONS.md` (the shared project, the in-memory task store with affinity, the result type over exceptions, card caching, and any further place the preview SDK lags 1.0); verify the sections exist
