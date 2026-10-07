## Stage A (now, beside extract-billing)

- [ ] A.1 `Maf.Lab.Plugins.Abstractions` (new files only): `IContributesWriteConfirmation`, `IWriteConfirmationFlow`,
      `WriteFlowOutcome` (`AskPerson`, `AskInput`, `TellModel`), `WriteProposal` (with `OpenInputs`),
      `WriteResolution`, `IStatesConfirmationFacts`, the ports `IWriteAudit`, `IConsultationScreening`,
      `IWriteTraceStep`, and `WriteConfirmationKeys` (five keys).
- [ ] A.2 `DatabaseInitializer`: a `RenamedTables` step (empty list) before the column renames. One `BEGIN IMMEDIATE`
      per table; it drops the old table's `IX_*` indexes, runs `RENAME TO`, and tolerates the loser's "no such table".
      A test renames a test table holding rows, with concurrent initializers.
- [ ] A.3 Web, added beside today's code (nothing removed yet):
      - `PendingWrite` in `web/src/api/types.ts`; the fee types stay until B.5;
      - a display-only `WriteSummary` component that renders a `<dl>` from the schema's titles, in property order, with
        `type`/`format` formatting;
      - `MafWebPlugin.confirmations`;
      - tests for each.
      `ConfirmationCard` and its consumers switch to them in B.5, with the server.
- [ ] A.4 Proof: one full parallel `dotnet test` run and `make test-web`; `openspec validate --strict --all`;
      `make docs-check`; the warnings-as-errors build.

## Stage B (after extract-billing is archived)

- [ ] B.1 `MafDbContext` maps `PendingWrites` (`FlowJson`; `ReviewTaskId`/`Questions` unmapped). `RenamedTables`
      gains `("PendingAdjustments", "PendingWrites")`, and `BackfillAsync` gains `awaiting_justification` →
      `awaiting_input` and the `FlowJson` copy. A test on a database holding old rows: they are kept, and a waiting one
      is still answerable.
- [ ] B.2 The core goes generic: `ConfirmationSink` (`maf-lab/write-*`, summary as `JsonElement`),
      `ConfirmationService` (`expired` on answer), `ChatTurnRunner` (`TurnResult.Proposal` → `PendingWrite`),
      `RunRejoin` (expiry check, `expired`), `HistoryEndpoints` (`/pending`, schema looked up by tool),
      `ToolSource` (`WriteConfirmationKeys`), `Program`, `MessageRetentionService`, `AuditChain`. Also the flow
      registry, the ports' adapters, sibling resolution as a core rule, and a tool with no flow refused.
- [ ] B.3 One pause builder (`PendingWrite` → `PersonQuestion`, without the state), used by the turn and by
      `RunRejoin.QuestionAsync` through `TurnContents.Ask`.
- [ ] B.4 Billing: `FeeAdjustmentFlow` on the seam through the ports. It owns `SummarySchema` and implements
      `IStatesConfirmationFacts`. Its server sends the `WriteConfirmationKeys`. Its web renderer for
      `propose_fee_adjustment`. The fee rules' tests pass unchanged.
- [ ] B.5 Web consumers: `useChatStream.ts`, `chatReducer.ts` (`interruptToConfirmation`), `ConfirmationCard.test.tsx`
      and `curriculum.ts` move to `PendingWrite`.
- [ ] B.6 Eval: `EvalAgentHost` and `ConfirmationSuite` through the seam and `IStatesConfirmationFacts`.
- [ ] B.7 Remove the six `CoreNamesNoDomainTests` entries marked "until the generalize-write-confirmation follow-up".
- [ ] B.8 Docs:
      - `docs/http-api.md`: the `/pending` shape, and the "state is deliberately absent" wording without fee terms;
      - `docs/plugins.md`;
      - DECISIONS: the next free section at landing, amending §17's rename sentence.
- [ ] B.9 Proof: one full parallel `dotnet test` run and `make test-web`; `openspec validate --strict --all`;
      `make docs-check`; the warnings-as-errors build.
