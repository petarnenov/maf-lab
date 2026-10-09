# The compliance plugin's make inputs (extract-compliance-plugin), read by the core Makefile
# (-include plugins/*/plugin.mk) while this folder exists. They are exported, so compose and every host-side run see them;
# `?=` keeps a value from the environment.

# How many reviewer replicas `make up` runs (its compose.yml reads it); two, so a task's follow-ups prove the routing.
export COMPLIANCE_REPLICAS ?= 2

# The eval consults the stack's reviewer through the balancer, as the api does (A2A:Clients is deployment configuration).
# Appended to the eval's own environment only, not exported: no other host-side run is told where a reviewer is.
EVAL_ENV += A2A__Clients__compliance__BaseUrl=$(BASE_URL)/compliance A2A__Clients__compliance__ClientId=maf-lab-assistant \
	A2A__Clients__compliance__ClientSecret=$${COMPLIANCE_CLIENT_SECRET:-assistant-dev-secret}
