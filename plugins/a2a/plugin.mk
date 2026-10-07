# The a2a plugin's make targets (extract-a2a), read by the core Makefile (-include plugins/*/plugin.mk). Progress and
# stopping are the probe's own: a line per scenario, and Ctrl+C between scenarios with exit 130.

.PHONY: eval-a2a

eval-a2a: require-dotnet ## Conformance: an outside client drives the agents through evals/a2a-conformance.jsonl
	@# Not $(EVAL): this one is deliberately not run by the harness, which links against the service. See DECISIONS.md.
	$(DOTNET) run --project tools/Maf.Lab.A2AProbe -- $(BASE_URL)
