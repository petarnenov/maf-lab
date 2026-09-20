# Tasks

## 1. What a conversation is waiting on

- [x] 1.1 Store the question and the expiry with a pending proposal, which the row does not keep today; verify a test that a proposal made through the flow can be read back with the sentence the tool composed and the expiry the signer set
- [x] 1.2 `GET /api/conversations/{id}/pending` returning the proposal still awaiting this person's answer, or nothing; verify tests: a waiting proposal comes back with account, amount, resulting fee, period, question and expiry; an applied, declined or expired one does not; another user's conversation is not found

## 2. The card

- [x] 2.1 The card's states on the turn (`waiting`, `answering`, `applied`, `declined`, `gone`, `expired`) in `chatReducer`, driven by the confirmation event and by the answer's outcome; verify Vitest on the reducer for each state without rendering
- [x] 2.2 `ConfirmationCard` rendered inline in the turn: account and name, current fee, amount, resulting fee, period, the server's question, Approve and Reject, and when it expires; verify Vitest: every field is on screen, the conversation around it is still readable, and nothing claims the adjustment was applied
- [x] 2.3 Answering: approve and reject each send a run that resumes the interrupt, the buttons are disabled while one is in flight, and the card settles into what it became; verify Vitest: approving posts a resume with `approve: true`, rejecting with `approve: false`, a second press while in flight sends nothing, and the answer's text appears in the conversation
- [x] 2.4 An expired proposal offers no answer and says it must be proposed again, and a server answer of "no longer waiting" puts the card in the same place; verify Vitest for both
- [x] 2.5 Opening a conversation asks what it is waiting on and renders the card from it; verify Vitest: a reloaded conversation shows the same proposal and can answer it, and one with nothing waiting shows no card

## 3. The rest of what the browser owes a person

- [x] 3.1 A compliance review's tool card says a review is under way and roughly how long it takes; verify a Vitest on `toolLabels`
- [x] 3.2 Three faces for three errors: `useChatStream` dispatches the kind it knows (recovered, unavailable, refused) and `ChatPage` renders each differently, with a way to send again where that helps; verify Vitest per kind, plus one asserting the rendered output carries no exception type, stack frame, hostname or query text
- [x] 3.3 The fourth feedback kind through all three places that must agree — `FeedbackKinds` and its allowlist in the domain, `FeedbackKind` in the web, the review queue's filter — offered only on a turn that asked for a confirmation; verify tests: the round trip stores and reads it back, the queue lists it, and the button is absent on a turn with no confirmation

## 4. Judging the summary

- [x] 4.1 `evals/confirmation.jsonl` and its loader case (account, amount, and the facts the summary must state); verify `EvalHarnessTests` validates the new dataset with the others
- [x] 4.2 A confirmation suite that proposes through the agent path, takes the summary from the interrupt and checks it states the account, the amount and the resulting fee the server computed, reporting the share that do; verify a test with one faithful summary and one that names an amount the proposal does not make
- [x] 4.3 Wire the suite into `make eval SUITE=confirmation`, the thresholds and the regression gate; verify the suite runs against the stack and its metric appears in the report

## 5. Verification and docs

- [x] 5.1 Live: propose an adjustment in the browser, see the card, approve one, reject another, reload a conversation with a proposal waiting and answer it after the reload, and watch a compliance review's card while it runs — record what the stack showed
- [x] 5.2 Run `make test`, `make lint`, `make verify`, `COMPOSE_PROJECT_NAME=maf-lab-ci make ci-e2e`, and the evals whose inputs moved
- [x] 5.3 Docs: `docs/http-api.md` (the pending-proposal endpoint and the fourth feedback kind), README (what a person sees when a write is waiting), `DECISIONS.md` §27 — the proposal rather than the run as what survives a closed tab, why the expiry is decided twice, and the deterministic confirmation suite; verify the sections exist
