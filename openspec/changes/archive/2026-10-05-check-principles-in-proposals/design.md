# Design

## Context

`scripts/docs.py` already checks each active proposal's `## Stopping` (`stopping_findings`) and `## Progress`
(`progress_findings`). It does so with `proposal_section` and `labelled`, which accept plain, list and bold labels, with
values continuing on indented lines.

## Decisions

**The same shape as its siblings.** `principles_findings` takes `(rel, change, text)` and returns findings. It is called
from `check_change_proposals` next to the other two.

**Accepted answers:**
- `None — <reason>`;
- `SOLID:` and `Standards:` with values;
- any number of `Own:` entries, each continuing over indented lines like a label.

Each `Own:` must name `DECISIONS §<n>` (or `DECISIONS.md §<n>`). The check does not open DECISIONS.md, because a
proposal names the section it will add before the section exists, just as a proposal names a follow-up change before
it exists.

**No judgement in the check.** Whether "SOLID:" says something true is review's work. The check guarantees only that
the question was answered and that every exception points at a recorded decision.

## Risks / Trade-offs

- [The section becomes boilerplate] → review rejects a `SOLID:` that names no concrete principle at work. The rule
  text asks a design to name the standard it stands on, not to recite the list.
