# The sample plugin's make targets (introduce-plugins 6.1), read by the core Makefile (-include plugins/*/plugin.mk) and
# joined to its targets as extra prerequisites: `make verify` asks the sample domain one question while the plugin is in
# use, and skips it otherwise. Progress and stopping are the check's own (tests/e2e.py).

.PHONY: verify-example

verify-example:
	python3 plugins/_example/tests/e2e.py $(BASE_URL)

verify: verify-example
