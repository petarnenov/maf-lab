# Design

## Context

`check-stop-in-proposals` added `stopping_findings` to `scripts/docs.py`: it finds the `## Stopping` section, drops
HTML comments, and reads labelled lines (optionally list items, optionally bold, with indented continuation lines). The
`## Progress` rule needs the same reading with different labels and a different verdict.

## Goals / Non-Goals

**Goals:** one way to read labelled sections; a `## Progress` check that fails a proposal saying nothing, and accepts
the three legitimate answers — none, how it shows, or not yet with a follow-up.

**Non-Goals:** judging the answer; checking that a `Not yet` follow-up change exists (it may not be proposed yet);
checking `design.md` for the "why" (review's).

## Decisions

**Extract `section(text, title)` and `labelled(body, labels)`.** `section` returns the section's body without HTML
comments, or `None` when there is no section; `labelled` returns `{label: value}` for the given labels, the first
`None`/`Not yet` line winning when present. `stopping_findings` is rewritten on them with no change in behaviour (its
tests stay as they are); `progress_findings` is new.

**The verdicts.** `None` needs a reason. `Not yet` needs a reason and a `follow-up:` with a name after it. Otherwise at
least one of `Terminal:` and `Page:` must be present, and any of them present must have a value. One finding per
problem, rule name `progress`.

**Order of checks.** Both rules run on every active proposal, so a proposal missing both sections learns both at once.

## Risks / Trade-offs

- [`None` is said for a change that does add a long action] → the check cannot know; review rejects it, as before.
- [The shared reader changes how `## Stopping` reads] → it is a refactor under the existing `StoppingTests`, which must
  pass unchanged.
