.PHONY: coverage testgen-e2e

coverage: require-docker ## Refresh the repository coverage snapshot at main through the running stack
	@plugins/coverage/files/coverage_refresh.sh $(BASE_URL)

testgen-e2e: ## Model-free test generation: refresh, run, verify and accept in the CI clone
	@resolved=$$($(PLUGINS_PY) resolve) || exit $$?; \
	if printf '%s\n' "$$resolved" | grep -qx coverage; then \
		plugins/coverage/files/testgen_e2e.sh $(BASE_URL); \
	else printf '%s\n' 'Test generation skipped: coverage is not installed.'; fi

plugin-e2e: testgen-e2e
