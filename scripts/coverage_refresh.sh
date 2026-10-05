#!/usr/bin/env bash
# make coverage: refresh the coverage snapshot at main through the running stack, and wait for it.
# The refresh runs in the coverage runner (both toolchains); this only asks for it, as an admin, and reports.
set -euo pipefail
BASE_URL="${1:-http://localhost:7171}"

if ! curl -sf "$BASE_URL/lb-health" >/dev/null 2>&1; then
  echo "✗ The stack is not running at $BASE_URL — start it with 'make' first."
  exit 1
fi

python3 - "$BASE_URL" <<'PY'
import json, signal, sys, time, urllib.request

base = sys.argv[1]

def req(path, method="GET", body=None, token=None):
    data = json.dumps(body).encode() if body is not None else None
    r = urllib.request.Request(base + path, data=data, method=method)
    r.add_header("Content-Type", "application/json")
    if token:
        r.add_header("Authorization", f"Bearer {token}")
    with urllib.request.urlopen(r, timeout=30) as resp:
        text = resp.read().decode()
        return json.loads(text) if text else None

token = req("/dev/token", "POST", {"userId": "alice", "tenantId": "firm-a", "role": "TENANT_ADMIN"})["token"]
job = req("/api/coverage/refresh", "POST", {}, token)

# Ctrl+C and SIGTERM (stop-anything): the refresh this started is cancelled on the way out, not left running.
def stop(signum, frame):
    try:
        req(f"/api/coverage/refresh/{job['jobId']}/cancel", "POST", {}, token)
    except Exception:
        pass
    print(f"\n✗ Cancelled. The coverage refresh (job {job['jobId']}) was stopped; run `make coverage` again.", flush=True)
    sys.exit(130)

signal.signal(signal.SIGINT, stop)
signal.signal(signal.SIGTERM, stop)
print(f"… measuring coverage at main (job {job['jobId']})", flush=True)
deadline = time.time() + 30 * 60
while job["state"] == "running" and time.time() < deadline:
    time.sleep(5)
    job = req(f"/api/coverage/refresh/{job['jobId']}", token=token)
if job["state"] != "succeeded":
    print(f"✗ Coverage refresh {job['state']}: {job.get('summary') or 'see make logs SERVICE=api'}")
    sys.exit(1)
print(f"✓ Coverage refreshed: {job['summary']}")
PY
