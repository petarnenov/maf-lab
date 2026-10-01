## 1. The pages rule in docs-check

- [x] 1.1 `scripts/docs.py`: read the page routes from `web/src/App.tsx` (not redirects, not `*`; parameter segments
      dropped) and report each one README.md does not name as `` `/path` ``, at the route's file and line
- [x] 1.2 `scripts/tests/test_docs.py`: a missing page fails with file, line and path; a named page passes; redirects,
      the catch-all and `:param?` segments are ignored; no `App.tsx` means no finding
- [x] 1.3 `python3 -m unittest discover -s scripts/tests -q` passes, and `make docs-check` on the unfixed README reports
      `/coverage` (the rule catches the drift that motivated it)

## 2. Documentation

- [x] 2.1 README.md: `/coverage` in Screens; `test-agent` and `coverage-runner` in the diagram and the replica sentence;
      the MCP servers' Jev calls in the diagram; published ports include the inspectors on loopback; a short section on
      coverage and the test-generation agent; the e2e row names test generation; the regression tolerance is per
      metric as `eval.json` configures it; the 0.21 → 0.68 figure is labelled as history; "Keeping docs in sync" names
      the pages rule and the `[routes.elsewhere]` exception kind
- [x] 2.2 Makefile: `up`'s `##` description names `PORTFOLIO_REPLICAS`
- [x] 2.3 `openspec/project.md`: container list includes `test-agent` and `coverage-runner`
- [x] 2.4 `.github/copilot-instructions.md`: all ten eval suites; a single-test example that matches a real test name
- [x] 2.5 Run `make docs` (never edit inside a `generated:` block by hand), then `make docs-check`
- [x] 2.6 Every relative link and every path README.md names exists; `openspec validate --specs --strict` passes
