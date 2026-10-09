.PHONY: eval eval-accept eval-selection eval-retrieval eval-generation eval-injection eval-confirmation eval-intent eval-guardrail eval-presentation eval-answer-check eval-code-route eval-graph-depth eval-a2a ask

# The eval's environment the installed plugins add to (their plugin.mk: EVAL_ENV += …), e.g. where a reviewer is.
EVAL_ENV ?=

eval: require-dotnet ## Run evals (SUITE=all|selection|retrieval|generation|injection|confirmation|intent|domain|presentation|guardrail|answer-check|code-route|graph-depth|generation-judge) against the stack's MCP servers
	Evals__McpEndpoint=$(BASE_URL)/mcp Evals__PortfolioMcpEndpoint=$(BASE_URL)/portfolio/mcp Evals__GatewayBaseUrl=$(BASE_URL) $(EVAL_ENV) $(HOST_ENV) $(DOTNET) run --project plugins/evals/service/runner/Maf.Lab.Evaluator.csproj -- --suite $(SUITE)$(if $(REPEAT), --repeat $(REPEAT))

EVAL_HOST = Evals__McpEndpoint=$(BASE_URL)/mcp Evals__PortfolioMcpEndpoint=$(BASE_URL)/portfolio/mcp Evals__GatewayBaseUrl=$(BASE_URL) $(EVAL_ENV) $(HOST_ENV) $(DOTNET) run --project plugins/evals/service/runner/Maf.Lab.Evaluator.csproj --
EVAL = $(EVAL_HOST) --suite

ask: require-dotnet ## Ask one question through the agent and print its trace (Q="…" TENANT=firm-a), e.g. a cross-domain one
	$(EVAL_HOST) --ask "$(Q)" --tenant $(or $(TENANT),$(FIRM),firm-a)

eval-accept: require-dotnet ## Accept an existing report (REPORT=runId), or run and accept a suite if REPORT is omitted
	$(if $(REPORT),$(EVAL_HOST) --accept-report $(REPORT),$(EVAL) $(SUITE) --accept-baseline$(if $(REPEAT), --repeat $(REPEAT)))

eval-selection: require-dotnet ## Eval: tool selection (recall/precision)
	$(EVAL) selection

eval-retrieval: require-dotnet ## Eval: retrieval (recall@5/@20, MRR per mode)
	$(EVAL) retrieval

eval-generation: require-dotnet ## Eval: answers graded by Jev, mean of 3 runs (REPEAT=N to change)
	$(EVAL) generation$(if $(REPEAT), --repeat $(REPEAT))

eval-injection: require-dotnet ## Eval: prompt-injection pass rate
	$(EVAL) injection

eval-confirmation: require-dotnet ## Eval: does the summary a person approves say what would happen
	$(EVAL) confirmation

eval-intent: require-dotnet ## Eval: intent classifier alone — would each question force search_documents? (needs JEV_MAF_LAB)
	$(EVAL) intent

eval-guardrail: require-dotnet ## Eval: content guard alone — are malicious prompts/tool results flagged and benign ones not? (needs JEV_MAF_LAB)
	$(EVAL) guardrail

eval-presentation: require-dotnet ## Eval: do portfolio answers build on their data cards instead of restating them?
	$(EVAL) presentation

eval-answer-check: require-dotnet ## Eval: Jev's answer check alone — are labelled unsupported answers flagged and supported ones not? (needs JEV_MAF_LAB)
	$(EVAL) answer-check

eval-code-route: require-dotnet ## Eval: Jev's code-route answer alone — would each code question start with the right graph call or the search? (needs JEV_MAF_LAB)
	$(EVAL) code-route

eval-graph-depth: require-dotnet ## Comparison: code graph traces at depth 2, 3 and 4, side by side, never gated (STRUCTURAL=1 for no model)
	$(EVAL) graph-depth $(if $(STRUCTURAL),--structural-only)


eval-a2a: require-dotnet ## Conformance: the outside A2A client checks the installed protocol hosts
	@resolved=$$($(PLUGINS_PY) resolve) || exit $$?; \
	if printf '%s\n' "$$resolved" | grep -qx a2a; then \
		$(DOTNET) run --project plugins/evals/service/probe/Maf.Lab.ProtocolProbe.csproj -- $(BASE_URL); \
	else printf '%s\n' 'A2A conformance skipped: the protocol plugin is not installed.'; fi

plugin-e2e: eval-a2a
