# Tasks

## 1. The code-route question and router

- [x] 1.1 Add `Jev:RouteCodeTools` (default true) and `Jev:MinCodeRouteConfidence` (default 0.6) to `JevOptions`.
      Verify with a unit test that binding reads both from configuration and that the defaults hold.
- [x] 1.2 Add `CodeToolRouter`:
      - the `code_need` Choice (D1: instructions, five options and their criteria);
      - `Read(answers)` → choice, probabilities and confidence;
      - `Route(question, answer, options, domains, toolNames)` → `(ToolRoute?, reason)`, with the D2 symbol and path
        patterns.

      Verify with unit tests:
      - one symbol, callers → `trace_code_symbol` `{symbol, direction: callers}`;
      - one symbol, callees → direction callees;
      - one path, impact → `change_impact` `{path}`;
      - a namespace-qualified symbol keeps `Type.Member`;
      - backticks are stripped;
      - a path's segments are not read as symbols;
      - each of these is not routed, with its reason: two symbols, two paths, no symbol, a confidence below the floor,
        text, none, codebase not primary, tool not offered, routing off;
      - Bulgarian and bg-latn questions that carry ASCII symbols are routed.
- [x] 1.3 Add the question to `JevIntentClassifier`'s single request when `RouteCodeTools` is on, and put the reading and
      the route on `IntentDecision`. Verify with a unit test over a fake Jev client: one request whose questions include
      `code_need` when on and exclude it when off, and the decision carries the route or the reason.

## 2. The turn

- [x] 2.1 In `ChatTurnRunner`, apply D4:
      - a code route alone becomes `route`, with `RequireSpecific` set to the graph tool;
      - beside other forced searches, the codebase search is dropped and the code route goes to `alongside`;
      - with no route, the turn behaves as today.

      Verify with unit tests that extend the existing forced-search tests:
      - a routed callers question issues `trace_code_symbol` first and never forces `search_codebase`;
      - a forcing intent with billing and codebase issues `search_documents` and the graph call together;
      - an unrouted codebase question still forces `search_codebase`;
      - chitchat forces nothing.
- [x] 2.2 Add the intent event's `codeRouting` payload and text line (D5). Verify with a turn-trace unit test: the routed
      tool, the arguments and the reason appear, and the question text does not appear in the log.

## 3. Eval

- [x] 3.1 Add the `CodeRouteCase` record and loader for `evals/code-route.jsonl`:
      - fields: expected option, `hasArgument`, language, split;
      - unknown values and missing fields are rejected, naming the row.

      Verify with loader unit tests.
- [x] 3.2 Write `evals/code-route.jsonl` (about 60 rows, per D6), with structural rows on real symbols and files that
      resolve in the code graph. Verify that the loader accepts it, that every structural row's symbol or path exists in
      the graph (a one-off `cypher-shell` check, noted in the PR), and that the per-language, per-split and per-class
      counts match D6.
- [x] 3.3 Implement `CodeRouteSuite`: the classifier alone, then `CodeToolRouter.Route`, with the metrics from the
      eval-harness delta, rows with and without an argument reported apart, failed requests reported apart, and one
      `ConsoleProgress` bar. Wire it into `Program` as a gated suite in `all`, like `intent`. Its thresholds go in `eval.json` and are set
      after the first run, from the design split (3.4). Add `make eval-code-route`. Verify with unit tests of the metric math over fake decisions, and with a live run.
- [x] 3.4 Tune `MinCodeRouteConfidence` on the design split (D3: text kept on design ≥ 0.95, then the highest
      structural recall), report holdout per language, set the `code-route` thresholds in `eval.json` from that run, and
      accept its baseline. Verify that the run passes its thresholds and that the chosen floor and its numbers are
      recorded in design.md's D3.
- [x] 3.5 Update selection cases s-79..s-82 to expect the graph tool, then re-run the `selection`, `intent`, `domain`,
      `generation` and `answer-check` suites. Verify that no metric drops beyond its tolerance except where these four
      cases explain it. Any accepted baseline move is named in the commit.
- [x] 3.6 Re-run `make eval-graph-depth` and report `graphToolCalled` per variant, before (0.46–0.58) and after.
      Verify that both reports are in `evals/reports/` and that the comparison is in the PR description.

## 4. Jev review

- [x] 4.1 Run the Jev review checklist (docs/rules/jev-usage.md §7) over `code_need` and the router:
      - closed and atomic;
      - not answerable by code;
      - one request;
      - minimal state;
      - an escape option;
      - gated on confidence with a fallback;
      - not a security boundary;
      - the model pinned and logged;
      - tested on labelled en, bg and bg-latn inputs.

      Verify that the filled checklist is in the PR description, with any item that does not hold fixed first.

## 5. Documentation

- [x] 5.1 README.md:
      - in "Evals — when you must run them", add `code-route` for the Jev routing questions and the
        `Jev:RouteCodeTools`/`Jev:MinCodeRouteConfidence` settings;
      - add `-code-route` to the `make eval-…` list.
- [x] 5.2 `docs/trace-events.md`: document the `intent` event's `codeRouting` object.
- [x] 5.3 `.github/copilot-instructions.md`: add `code-route` to the eval CLI's suite list.
- [x] 5.4 Run `make docs` to regenerate the `generated:make-targets` block, then `make docs-check`. Verify that both pass.
