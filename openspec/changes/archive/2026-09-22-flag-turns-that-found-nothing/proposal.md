# Proposal

## Why

A turn that answered correctly, with five sources under it, was flagged for review as having
retrieved nothing.

Observed on the running stack, conversation `c_c480e8ac60ef406aa70e7a35f70f88fb`, asked in
Bulgarian: "Каква е процедурата при липсваща фий схема?"

- The forced search recorded `translationNote: "translation was not usable"`. The query went to the
  store in Bulgarian, all six of its terms were outside the BM25 vocabulary, and the dense
  candidates fell below the relevance floor. **0 fused candidates.**
- The model then searched again, in English, for "procedure when a fee schedule is missing", and
  got **20**.
- The turn finished with **5 sources** and a correct answer.
- And still: `signals=['zero_retrieval_results']`.

The condition is wider than the name. `ChatTurnRunner` sets a flag per tool call:

```csharp
if (name == "search_documents" && !isError && sources.Count == 0)
{
    state.ZeroResults = true;
}
```

Once set it is never cleared, so a turn that searched twice and succeeded on the second is
indistinguishable from a turn that found nothing at all.

This did not matter until today. Before the relevance floor landed, an approximate-nearest-neighbour
search always returned its k nearest neighbours, so `sources.Count == 0` was unreachable and the
signal never fired on a real turn. Making it reachable immediately exposed that it asks the wrong
question.

The review queue exists so a reviewer can label turns that went wrong, and the labels it collects
are appended to the eval datasets. A turn that recovered and cited its sources has nothing to
label. Left as is, the queue fills with turns that are already right, and the ones that are wrong
get harder to find.

## What Changes

- The `zero_retrieval_results` signal is raised for a turn that searched and finished with no
  sources at all, not for a turn in which any one search came back empty.
- A turn whose first search found nothing and whose second found documentation does not carry the
  signal. It answered; there is nothing for a reviewer to do with it.
- A turn that searched and ended with nothing still carries it, exactly as now.

The information that made the first search fail — the unusable translation — is not lost. It is in
the turn's trace, and the Retrieval tab already shows both the note and the candidates the floor
dropped. Whether it deserves a signal of its own is answered in design.md: it does not.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `web-ui`: "Feedback review queue" — its "Zero-result turn flagged" scenario says "a turn's
  `search_documents` call returned zero results", which is the condition being corrected. The
  requirement's list of signals stays as it is; what changes is when this one fires.

## Impact

- `src/Maf.Lab.Api/Agent/ChatTurnRunner.cs` — the per-call flag becomes "this turn searched".
- `src/Maf.Lab.Api/Agent/TurnSignals.cs` — the signal is computed from that and the turn's own
  source count, which `Compute` already receives.
- `tests/Maf.Lab.Tests/ZeroResultTurnTests.cs`, `tests/Maf.Lab.Tests/AgentUnitTests.cs`,
  `tests/Maf.Lab.Tests/FeedbackApiTests.cs` — the existing signal tests, plus the case that is
  wrong today: a turn that searched twice and cited sources.
- `openspec/specs/web-ui/spec.md` on sync.
- No API change, no schema change, no dependency, no package version moves.
