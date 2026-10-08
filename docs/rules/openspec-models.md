---
name: openspec-models
description: Which Claude model does which OpenSpec phase and task, at what effort, and who takes over when a model fails — two tries per model, then one model up, then the human. Read before starting an OpenSpec phase or delegating work to a subagent.
applies_to: Claude Code sessions and subagents working on openspec/changes/* (explore, propose, apply, verify, sync, archive)
based_on: Claude model table of 2026-10-06 (Fable 5.1, Opus 5.5, Sonnet 5.5, Haiku 5.5); OpenSpec skills (core + expanded profile)
---

# Rule: OpenSpec work by model

This is about the models that build maf-lab, not the ones it runs (`gpt-oss:120b`, `embeddinggemma`, Jev).

## 0. TL;DR

1. **Fable thinks, Opus solves the hard parts, Sonnet builds, Haiku does the mechanical work.**
2. Pay for depth where a mistake multiplies: specs and design. Save in apply.
3. Every task in `tasks.md` carries a class — `[mech]`, `[std]` or `[hard]` — and the class picks the model.
4. **Two tries per model, then one model up:** Haiku ×2 → Sonnet ×2 → Opus ×2 → Fable ×2 → the human.
5. The second try on a model raises effort one level and changes the hypothesis, never only the wording.
6. Thinking does not cross models and the prompt cache is per model: one model per phase, and a text
   handoff on every switch.

## 1. Models

| Model | in / out $ per MTok | vs Sonnet | Strength | Default effort |
|---|---|---|---|---|
| Fable 5.1 | 10 / 50 | ×5 | deepest reasoning, long horizons, unclear requirements | thinking always on |
| Opus 5.5 | 4 / 20 | ×2 | hard code, architecture, review | `medium` — set it explicitly |
| Sonnet 5.5 | 2 / 10 | ×1 | everyday code, tests, agent loops | `high` — start at `medium` |
| Haiku 5.5 | 0.10 / 0.50 | ×0.05 | mechanical edits, lookups, formatting | `medium` |

Fable turns can run for minutes: it is for thinking phases, not for an iterative apply.

## 2. Risk tier of a change

Decide it when the change is proposed and write it in `proposal.md`. When torn, take the higher one.

| Tier | maf-lab examples |
|---|---|
| LOW | docs, copy, CSS, one isolated component, a test-only change |
| MEDIUM | a new MCP tool or endpoint, a new page with TanStack Query, an eval suite, a new make target |
| HIGH | tenant isolation (the Principal-bound Qdrant and Cypher methods), auth and tokens, write confirmation (MRTR `input_required`), prompt-injection defenses, Jev guardrails and thresholds, AG-UI mappings, compose/lb topology, index profile or vector schema, package version moves |

## 3. Phase → model

| OpenSpec skill / artifact | LOW | MEDIUM | HIGH |
|---|---|---|---|
| `/openspec-explore` | Sonnet · medium | Opus · high | Fable · high |
| ↳ code lookups (read-only subagents) | Haiku | Haiku | Haiku |
| `/openspec-propose` · `proposal.md` | Sonnet | Opus · high | Fable · high |
| ↳ `specs/` (requirements, scenarios) | Sonnet | Fable · medium | Fable · high |
| ↳ `design.md` | — | Opus · high | Fable · xhigh |
| ↳ `tasks.md` (with the class tags) | Sonnet | Opus · medium | Opus · high |
| `openspec validate --strict`, `make docs-check` | no model | no model | no model |
| ↳ fixing their findings | Haiku | Haiku | Haiku |
| `/openspec-apply-change` | by task class, §4 | | |
| the verify pass and the archive review | Sonnet | Opus · high | Opus · high, then a Fable pass spec ↔ code |
| `/openspec-sync-specs`, `/openspec-archive-change` | Haiku | Haiku | Haiku, checked by Sonnet |

The reviewer is never the model that wrote the code.

## 4. Task classes in apply

| Class | maf-lab examples | Model · effort |
|---|---|---|
| `[mech]` | DTO fields, renames, docs sources for `make docs`, CSS modules, make target wiring, compose env lines | Haiku · medium |
| `[std]` | an `AIFunction` tool per the design, a minimal-API endpoint, a React component with TanStack Query, xUnit and Vitest tests | Sonnet · medium |
| `[hard]` | the tenant-filtered query method, Cypher templates, BM25 and fusion math, AG-UI mappings, cancellation (stop-anything), MCP transport gaps, Jev request design | Opus · high (xhigh for tenant and security) |

A task whose answer nobody knows yet (an undocumented API, a bug without a repro) is not an apply task: it
goes back to explore.

## 5. When a model fails — who takes over

```
Haiku ×2 ──► Sonnet ×2 ──► Opus ×2 ──► Fable ×2 ──► human
```

| Try on the model | What runs |
|---|---|
| 1 | the model from §3 or §4, at its effort |
| 2 | the same model, effort one level up, a new hypothesis and the output of try 1 |
| after 2 failures | the next model up starts at try 1, with the handoff from §6 |
| Fable 2 of 2 | stop and report to the human: blocker, both hypotheses per model, real output, options |

| Failed twice | Takes over | Except when |
|---|---|---|
| Haiku | Sonnet · medium | — |
| Sonnet | Opus · high | the spec is unclear → back to propose on Fable, not up |
| Opus | Fable · high (try 2: xhigh) | an environment, credential or product question → the human at once |
| Fable | the human | a new Fable try only with new information from the human |

Skip the ladder when:
- **the spec or a scenario is unclear or contradicts itself** → back to propose on Fable, whichever model was
  applying;
- **the same test stays red under two different models** → the test or the design is wrong: Opus reviews them,
  no new writer;
- **the environment is the blocker** (a container down, `OLLAMA_API_KEY` or `JEV_MAF_LAB` missing) → the human,
  no tries spent;
- **the model refuses** (`stop_reason: refusal`) → the server-side fallback, not the ladder.

A failure is one of: a red `make test` / `make lint` / `make verify` / `make docs-check`, an `openspec
validate` error, a blocking review finding, files changed outside the task, or the model saying it is not sure.
Never get past a failure by skipping a test or loosening a check: that is an escalation, not a fix.

## 6. Handoff on a model switch

Thinking does not cross models, so every switch carries this text:

1. change id, task id and class;
2. the requirement and scenario from `specs/`, verbatim;
3. the failed diff (`git diff <base> -- <files>`);
4. the failing output: command, exit code, summary line, path to the full log;
5. the hypotheses already ruled out, one line each.

## 7. How to pick the model in Claude Code

- Main session: `/model` before the phase (Fable for explore and specs on HIGH, Sonnet for apply).
- Subagents: the Agent tool's `model` argument (`fable`, `opus`, `sonnet`, `haiku`) — it outranks the agent's
  frontmatter, so moving up the ladder never edits an agent file.
- Effort: set it explicitly per phase; Opus 5.5 and Haiku 5.5 default to `medium`.
