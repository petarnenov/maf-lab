## 1. The seam

- [ ] 1.1 `Maf.Lab.Plugins.Abstractions`: `IContributesWriteConfirmation`, `IWriteConfirmationFlow`, `WriteFlowOutcome`,
      `WriteProposal`, `WriteResolution`; the ports `IWriteAudit`, `IPromptScreening`, `IWriteTraceStep` (the reviewer port
      is extract-billing part 3's).
- [ ] 1.2 The core: adapters for the three ports (over `ToolAudit`, `Guardrail`, `TurnTrace`) and a flow registry
      built from the installed plugins, keyed by tool name.

## 2. The store

- [ ] 2.1 `PendingAdjustments` → `PendingWrites`: rename in place, `awaiting_justification` → `awaiting_input`,
      `ReviewTaskId`/`Questions` → `FlowJson`, by the mechanism the architect rules (open question 1).
- [ ] 2.2 A test on a database holding old rows: they are kept and a waiting one is still answerable; two replicas
      starting together migrate once.

## 3. The core goes generic

- [ ] 3.1 `ConfirmationSink`/`CapturedConfirmation` read `maf-lab/write-*` and carry the summary as `JsonElement`
      with its schema.
- [ ] 3.2 `ConfirmationService`, `ChatTurnRunner` (`TurnResult.Proposal` → `PendingWrite`, `ProposedAsync`), `RunRejoin`,
      `HistoryEndpoints` (`/pending`), `ToolSource`, `Program`, `MessageRetentionService`, `AuditChain`: no fee type left.
- [ ] 3.3 A write tool with no flow is refused and the model is told.
- [ ] 3.4 An architecture test: no core project names `FeeAdjustment*` or `PendingAdjustment*`.

## 4. Billing on the seam

- [ ] 4.1 `FeeAdjustmentFlow` implements `IWriteConfirmationFlow` for `propose_fee_adjustment` through the ports;
      billing contributes it. The fee rules' tests pass unchanged.
- [ ] 4.2 Billing's server sends `maf-lab/write-*` and the summary's schema.

## 5. Web, eval, docs

- [ ] 5.1 `web/src/api/types.ts`: `PendingWrite`; `ConfirmationCard` renders by schema; `MafWebPlugin.confirmations`;
      billing's renderer for `propose_fee_adjustment`.
- [ ] 5.2 `EvalAgentHost` and `ConfirmationSuite` through the seam (`FactsToState`).
- [ ] 5.3 `docs/http-api.md` (`/pending`), `docs/plugins.md`, DECISIONS.md section.

## 6. Verify

- [ ] 6.1 One full parallel `dotnet test --solution maf-lab.sln` run and `make test-web` (the user's one-run rule).
- [ ] 6.2 `openspec validate --strict --all`, `make docs-check` and the warnings-as-errors build.
