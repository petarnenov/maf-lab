# Proposal

## Why

An audit of README.md against the code, claim by claim, found statements that the code no longer supports. None of
them is caught by `make docs-check`, which checks generated blocks, api routes, `make` references, model names, links
and proposals, and nothing else:

- **Wrong.** "Only Qdrant and Ollama are published besides 7171": since `run-inspectors-with-the-stack`, the inspectors
  are published too, on `127.0.0.1:7172–7174` (`compose/docker-compose.yml`, `lb` and `redis-insight` ports).
- **Stale.** The eval regression tolerance: README says `retrieval` uses a per-suite 0.03. Since "Judge each metric
  against its own noise" the overrides are per metric — `recall@5:bg` and `recall@20` in retrieval at 0.025,
  `relevance` in generation at 0.035 (`src/Maf.Lab.Eval/eval.json` → `Evals:RegressionTolerances`).
- **Stale.** "Bulgarian recall@5 went from 0.21 to 0.68 while English stayed at 0.69" reads as the current state; the
  accepted baseline is now 0.85 and 0.77 (`evals/baseline.json`, retrieval/hybrid). It is the measurement from when
  query translation was introduced.
- **Missing.** The Screens list omits `/coverage`, a page in the main navigation (`web/src/App.tsx`,
  `web/src/components/Layout.tsx`).
- **Missing.** The architecture diagram, which README says is "the same picture" as `docs/topology.drawio`, has no
  `test-agent` and no `coverage-runner`; the drawing and the compose file have both. The replica sentence omits them
  too, and the diagram gives Jev no caller but the api although the MCP servers call it (relevance judge, warm-up).
- **Missing.** Coverage and the test-generation agent — six specs, two services, a page and two make targets — have no
  README section; README mentions them only in passing.
- **Missing.** The CI table's e2e row omits test generation, which `make ci-e2e` runs (`scripts/ci_e2e.sh`:
  `testgen-e2e`).
- **Missing.** "Keeping docs in sync" lists three kinds of exception in `docs/docs-sync.toml` and omits the fourth,
  `[routes.elsewhere]` (a route another host serves behind the same balancer).
- **Stale (generated source).** `make up`'s description names `API_REPLICAS/MCP_REPLICAS/COMPLIANCE_REPLICAS` and not
  `PORTFOLIO_REPLICAS`, which it also scales.

The same audit found the same kind of drift in documents README points to: `openspec/project.md`'s container list
omits `test-agent` and `coverage-runner`; the Copilot instructions name five eval suites of ten, and their single-test
example filters `ChatPage.test.tsx` by a name no test in that file has.

One class of these is mechanical: a page the web app registers and README does not name. The check can see that, so
it should.

## What Changes

- README.md: fix every finding above. Prefer the code's facts; keep the history (the 0.21 → 0.68 measurement) labelled
  as history and point at the baseline for the current figures.
- Makefile: `make up`'s `##` description names `PORTFOLIO_REPLICAS`; `make docs` carries it into the README block.
- `openspec/project.md`: the container list includes `test-agent` and `coverage-runner` (`make docs` copies it into
  `openspec/config.yaml`).
- `.github/copilot-instructions.md`: the eval suite list and the single-test example match the code.
- `scripts/docs.py`: a new rule, **pages** — every page route registered in `web/src/App.tsx` (redirects and the
  catch-all excepted) must be named in README.md as `` `/path` ``. Unit tests in `scripts/tests/test_docs.py`.

No application behaviour changes. No package moves.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `documentation-sync`: "The documentation check" gains one condition — a page the web app registers that README.md
  does not name.

## Impact

- Documents: README.md, `openspec/project.md` (and through `make docs`, `openspec/config.yaml`),
  `.github/copilot-instructions.md`.
- Tooling: `scripts/docs.py`, `scripts/tests/test_docs.py`, the Makefile's `up` description. `make docs-check` gains one
  rule; it stays standard-library Python and fast.
- Progress feedback: no new CLI tool or UI action; `make docs-check` remains a sub-second check that prints its
  one-line summary.
- No Jev call is added or changed.

## Documentation impact

- README.md — the fixes listed under Why; "Keeping docs in sync" lists the new pages rule and the fourth exception kind.
- `openspec/project.md` — container list (and `openspec/config.yaml` via `make docs`).
- `.github/copilot-instructions.md` — eval suite list, single-test example.
- CLAUDE.md, `docs/*.md` — audited, nothing untrue found; unchanged.
