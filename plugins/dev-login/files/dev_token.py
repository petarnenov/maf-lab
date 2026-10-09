#!/usr/bin/env python3
"""One dev/qa persona token. stdout contains only the token for command substitution."""
import argparse
import json
import sys
import urllib.error
import urllib.request

p = argparse.ArgumentParser()
p.add_argument("--base-url", required=True)
p.add_argument("--persona", required=True)
p.add_argument("--tenant")
p.add_argument("--audience", default="api")
a = p.parse_args()
body = {"persona": a.persona, "audience": a.audience}
if a.tenant:
    body["tenantId"] = a.tenant
try:
    req = urllib.request.Request(a.base_url.rstrip("/") + "/dev/token", json.dumps(body).encode(), {"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=10) as reply:
        token = json.load(reply)["token"]
    print(token)
except (urllib.error.URLError, KeyError, ValueError):
    print("Could not obtain the requested development token.", file=sys.stderr)
    sys.exit(1)
except KeyboardInterrupt:
    print("Cancelled.", file=sys.stderr)
    sys.exit(130)
