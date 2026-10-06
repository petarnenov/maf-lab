# Spec Delta

## MODIFIED Requirements

### Requirement: A code answer is judged as code
When this turn's sources or the previous turn's sources include the snippets of a search whose domain's guard context
is code (the codebase search), the check SHALL ask its two questions in a code context and SHALL otherwise use the
documents context (named `billing` before introduce-plugins) unchanged. The trace names the context `code` or
`documents`. The codebase
context SHALL say that the user asked about the lab's own repository, that each codebase source reads as its place —
path, line range and symbol — followed by the code, and that other sources may be document excerpts and records. The
grounding criteria SHALL count as supported a path, file name, line range, symbol, identifier or quoted code that a
source carries. They SHALL count an explanation as supported when a source's code or text shows what it states. They
SHALL judge meaning, not wording, so an answer in Bulgarian or Latin-script Bulgarian about English code and comments
counts as supported when the code supports its meaning. A line range SHALL count as unsupported when no source covers
it.

#### Scenario: A Bulgarian explanation of English code (high confidence)
- **WHEN** a Bulgarian question about how the content guard withholds an excerpt is answered in Bulgarian, citing `src/Maf.Lab.Api/Agent/Guardrail.cs:153-195`, and that snippet is among the sources
- **THEN** the request uses the codebase context, and a grounding probability at or above the pass threshold yields `pass`

#### Scenario: A cited line range no source covers (low confidence)
- **WHEN** a code answer cites `src/Maf.Lab.CodeSearch/Program.cs:120-140` and no source covers those lines, and Jev's grounding probability is below the signal floor
- **THEN** the verdict is `not_grounded` and the turn carries `answer_not_grounded`

#### Scenario: A code answer Jev is unsure about (medium confidence)
- **WHEN** a code answer's grounding probability falls inside the review band
- **THEN** the verdict is `uncertain`, with no review signal

#### Scenario: A billing turn keeps its context
- **WHEN** a turn read only `search_documents` excerpts and billing records
- **THEN** the request uses the documents context, exactly as before this change, and the trace names it `documents`

#### Scenario: The codebase check is unavailable
- **WHEN** the answer check of a code answer times out
- **THEN** the verdict is `unchecked` with the reason, no signal is added, and the turn completes as before
