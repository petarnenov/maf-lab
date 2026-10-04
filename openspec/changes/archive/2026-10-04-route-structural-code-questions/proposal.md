# Proposal

## Why

The graph-depth comparison (add-graph-depth-eval) found that the agent calls a code graph tool in only about half of
the structural code questions (graphToolCalled 0.46–0.58), so the depth of a trace cannot matter in the other half.

The cause is not the tool descriptions. `ChatTurnRunner.CodebaseSearch` forces `search_codebase` as the first call
for every question whose primary domain is the codebase, unless it is small talk. Its stated reason, "the codebase has
no read tools", was true before `trace_code_symbol` and `change_impact` existed. The forced call is issued by code on
the model's behalf, because Ollama ignores tool choice, and after it the model often answers from the snippets. Who
calls a method, what it calls, and which tests cover a file are then answered from text search, which cannot see calls.

## What Changes

- **A new question in the intent request.** It is one closed Choice, asked in the same Jev request with the same
  state, so it costs no extra round trip. It asks what a codebase question needs:
  - the callers of a named symbol;
  - its callees;
  - the impact of a named file;
  - the code's text;
  - none of these.
- **Routing in code.** Code turns a structural answer into a routed graph call, issued on the model's behalf like today's
  forced search, only when all of these hold:
  - the codebase is the primary domain in scope and the intent is not chitchat (the condition that forces the search
    today);
  - Jev's answer reaches a configured confidence floor;
  - the graph tool is offered;
  - the call's argument comes from the question through fixed patterns: exactly one `Type.Member` symbol for a trace,
    or exactly one repository C# path for an impact.

  The direction of a trace comes from Jev's answer, callers or callees. Cypher still never comes from a request: the
  tool takes a symbol or a path, as it does today.
- **Everything else stays as today.** A low-confidence answer, a text question, no symbol or path in the question, more
  than one candidate, or a tool not offered all leave the turn exactly as it is now, with `search_codebase` forced. The
  reason is kept.
- **The trace.** The turn trace's intent event carries the code-route answer and the reason a question was or was not
  routed.
- **Configuration.** Code routing is configuration and can be switched off. Its confidence floor is configuration too,
  tuned on the labelled design split and reported on the holdout split.
- **A new Jev-alone eval suite, `code-route`, over `evals/code-route.jsonl`.** It holds labelled questions in English,
  Bulgarian and Bulgarian in Latin letters, split into design and holdout. It measures:
  - accuracy;
  - structural recall: structural questions routed to the right tool;
  - how often text questions are correctly left alone.

  It is reported per language and split. It shows its progress with the shared progress bar. `make eval-code-route`.
- **Selection dataset.** Cases s-79 to s-82 now expect the graph tool, not `search_codebase` plus the graph tool.
- **Graph-depth re-run.** graph-depth is re-run, and its tool-call share is reported before and after.
- **Spec correction.** The codebase-search requirement that only `search_codebase` is offered to the agent is corrected.
  The api already offers all three codebase tools (`src/Maf.Lab.Api/appsettings.json`).

**Why Jev and not code or the LLM** (docs/rules/jev-usage.md §2 and §5):
- **Not code.** Telling "who calls X" from "how does X work" is a language judgment, in three spellings (en, bg,
  bg-latn), and a keyword list fails on paraphrase.
- **Not the LLM.** The model is what skips the graph today, and asking it first would add a model round trip.
- **Jev fits.** The answer space is closed (five options); code acts on it with a branch and a threshold; the action is
  read-only; and it rides in a request the turn already sends.
- **What stays in code.** Symbol and path extraction are regex-able, so they stay in code (§5): Jev picks the tool and
  the direction, code takes the argument.

## Capabilities

### New Capabilities
<!-- none -->

### Modified Capabilities
- `intent-classification`: the classification request carries the code-route question. A codebase question is routed
  to a code graph tool when Jev, the confidence floor and the fixed patterns agree, and is otherwise searched as today.
- `codebase-search`: the codebase server offers the agent its search and both graph tools. The requirement that it
  offers only `search_codebase` is out of date.
- `eval-harness`: adds the `code-route` suite, its dataset and its metrics.

## Impact

- `src/Maf.Lab.Api/Agent/Jev/`:
  - the code-route question and its reading, in a new `CodeToolRouter` beside `DataToolRouter`;
  - added to `JevIntentClassifier`'s single request.
- `src/Maf.Lab.Api/Agent/ChatTurnRunner.cs`: the codebase's first call is the routed graph call or, as today, the forced
  search. `RequiredToolModeChatClient` issues the routed call with its arguments, as it already does for data routes.
- `src/Maf.Lab.Retrieval/Jev/JevOptions.cs`: `RouteCodeTools` (on) and `MinCodeRouteConfidence`.
- `src/Maf.Lab.Eval`: the `CodeRouteSuite`, a dataset record and loader, the program wiring, and a progress bar.
  `Makefile`: the `eval-code-route` target and the suite in the `eval` help line.
- Datasets:
  - `evals/code-route.jsonl`: new;
  - `evals/selection.jsonl`: s-79 to s-82 updated;
  - `evals/baseline.json`: re-accepted for selection only if the change moves it, and noted in the commit.
- No package version moves.
- **Behaviour change.** Structural code questions now start with a graph call instead of a search. The model can still
  call `search_codebase` after it.
- **Progress.** The new suite shows a determinate progress bar over its cases, with done/total and a percentage, and
  plain lines when its output is not a terminal (progress-feedback).

## Documentation impact

- `README.md`:
  - "Evals — when you must run them" gains `code-route` beside `intent` for a change to the Jev routing questions or
    `Jev:RouteCodeTools`/`Jev:MinCodeRouteConfidence`;
  - the make-target table gains `eval-code-route` through `make docs`.
- `docs/trace-events.md`: the `intent` event's payload gains its `codeRouting` object, which holds Jev's choice, its
  confidence, the routed tool, its arguments, and the reason.
- `.github/copilot-instructions.md`: the eval CLI's suite list gains `code-route`.
- `CLAUDE.md`, `openspec/project.md`: not affected. They describe Jev's role and the intent classifier in terms that stay
  true.
