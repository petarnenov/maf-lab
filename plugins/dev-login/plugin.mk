.PHONY: dev-token
PERSONA ?= adam
AUDIENCE ?= api
dev-token: require-python ## Issue one development persona token (PERSONA= TENANT= AUDIENCE=); stdout is the token
	@python3 plugins/dev-login/files/dev_token.py --base-url "$(BASE_URL)" --persona "$(PERSONA)" --audience "$(AUDIENCE)" $(if $(TENANT),--tenant "$(TENANT)",)
