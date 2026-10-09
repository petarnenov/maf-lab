.PHONY: tenant-allow
ENABLE ?= 0
tenant-allow: require-python ## Allow a plugin through a tenant-scoped operator token (TENANT= PLUGIN= ENABLE=1); MAF_BEARER_TOKEN overrides dev issuance
	@python3 plugins/platform-admin/files/tenant_allow.py --base-url "$(BASE_URL)" --tenant "$(TENANT)" --plugin "$(PLUGIN)" --enable "$(ENABLE)"
