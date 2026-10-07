## Stage A (now, beside extract-billing)

- [x] A.1 `Maf.Lab.Plugins.Abstractions` (new files only): `IContributesWriteConfirmation`, `IWriteConfirmationFlow`,
      `WriteFlowOutcome` (`AskPerson`, `AskInput`, `TellModel`), `WriteProposal` (with `OpenInputs`),
      `WriteResolution`, `IStatesConfirmationFacts`, the ports `IWriteAudit`, `IConsultationScreening`,
      `IWriteTraceStep`, and `WriteConfirmationKeys` (five keys).
- [x] A.2 `DatabaseInitializer`: a `RenamedTables` step (empty list) before the column renames. One `BEGIN IMMEDIATE`
      per table; it drops the old table's `IX_*` indexes, runs `RENAME TO`, and tolerates the loser's "no such table".
      A test renames a test table holding rows, with concurrent initializers.
- [x] A.3 Web, added beside today's code (nothing removed yet):
      - `PendingWrite` in `web/src/api/types.ts`; the fee types stay until B.5;
      - a display-only `WriteSummary` component that renders a `<dl>` from the schema's titles, in property order, with
        `type`/`format` formatting;
      - `MafWebPlugin.confirmations`;
      - tests for each.
      `ConfirmationCard` and its consumers switch to them in B.5, with the server.
- [x] A.4 Proof: one full parallel `dotnet test` run and `make test-web`; `openspec validate --strict --all`;
      `make docs-check`; the warnings-as-errors build. Done: 1842 passed, 0 failed; the web suite green; lint and `tsc -b` clean.

## Stage B (after extract-billing is archived)

Order: B.1 lands the `PendingAdjustments` → `PendingWrites` entry, the backfills and the `MafDbContext` mapping in
one commit (never the entry alone); B.5 switches `ConfirmationCard` and its consumers and removes the fee types.


- [x] B.1 `MafDbContext` maps `PendingWrites` (`FlowJson`; `ReviewTaskId`/`Questions` unmapped). `RenamedTables`
      gains `("PendingAdjustments", "PendingWrites")`, and `BackfillAsync` gains `awaiting_justification` →
      `awaiting_input` and the `FlowJson` copy. A test on a database holding old rows: they are kept, and a waiting one
      is still answerable.
- [x] B.2 The core goes generic: `ConfirmationSink` (`maf-lab/write-*`, summary as `JsonElement`),
      `ConfirmationService` (`expired` on answer), `ChatTurnRunner` (`TurnResult.Proposal` → `PendingWrite`),
      `RunRejoin` (expiry check, `expired`), `HistoryEndpoints` (`/pending`, schema looked up by tool),
      `ToolSource` (`WriteConfirmationKeys`), `Program`, `MessageRetentionService`, `AuditChain`. Also the flow
      registry, the ports' adapters, sibling resolution as a core rule, and a tool with no flow refused.
- [x] B.3 One pause builder (`PendingWrite` → `PersonQuestion`, without the state), used by the turn and by
      `RunRejoin.QuestionAsync` through `TurnContents.Ask`.
- [x] B.4 Billing: `FeeAdjustmentFlow` on the seam through the ports. It owns `SummarySchema` and implements
      `IStatesConfirmationFacts`. Its server sends the `WriteConfirmationKeys`. Its web renderer for
      `propose_fee_adjustment`. The fee rules' tests pass unchanged.
      One assertion in the flow's test pins the `FlowJson` shape it reads (`{reviewTaskId, questions}`) to the shape
      B.1's backfill writes, so the migration and the reader cannot drift.
- [x] B.5 Web consumers: `useChatStream.ts`, `chatReducer.ts` (`interruptToConfirmation`), `ConfirmationCard.test.tsx`
      and `curriculum.ts` move to `PendingWrite`.
- [x] B.6 Eval: `EvalAgentHost` and `ConfirmationSuite` through the seam and `IStatesConfirmationFacts`.
- [x] B.7 Remove the six `CoreNamesNoDomainTests` entries marked "until the generalize-write-confirmation follow-up".
- [x] B.8 Docs:
      - `docs/http-api.md`: the `/pending` shape, and the "state is deliberately absent" wording without fee terms;
      - `docs/plugins.md`;
      - DECISIONS: the next free section at landing, amending §17's rename sentence.
- [x] B.9 Proof: one full parallel `dotnet test` run and `make test-web`; `openspec validate --strict --all`;
      `make docs-check`; the warnings-as-errors build.
      Done: with plugins/billing present, 1854 .NET + 700 web passed; moved aside, the core builds and passes but for
      two load-flaky tests that pass alone and on the clean tip.
