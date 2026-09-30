#!/usr/bin/env bash
# Model-free end-to-end test generation (make ci-e2e): refresh coverage, raise the fixture's threshold with a run of the
# test agent (the CI stub writes its test), wait for the verified candidate, accept it, and check the file's coverage.
# It runs against the throwaway repository ci-e2e mounts (MAF_LAB_REPO), never against your own checkout's main.
set -euo pipefail
BASE_URL="${1:-http://localhost:7171}"
TARGET="src/Maf.Lab.Api/Coverage/Fixtures/E2eTarget.cs"

"$(dirname "$0")/coverage_refresh.sh" "$BASE_URL"

python3 - "$BASE_URL" "$TARGET" <<'PY'
import json, sys, time, urllib.error, urllib.parse, urllib.request

base, target = sys.argv[1], sys.argv[2]
failures = []

def req(path, method="GET", body=None, token=None):
    data = json.dumps(body).encode() if body is not None else None
    r = urllib.request.Request(base + path, data=data, method=method)
    r.add_header("Content-Type", "application/json")
    if token:
        r.add_header("Authorization", f"Bearer {token}")
    try:
        with urllib.request.urlopen(r, timeout=60) as resp:
            text = resp.read().decode()
            return resp.status, (json.loads(text) if text else None)
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode()

def check(name, ok, detail=""):
    print(("✓ " if ok else "✗ ") + name + (f" — {detail}" if detail and not ok else ""), flush=True)
    if not ok:
        failures.append(name)

_, t = req("/dev/token", "POST", {"userId": "alice", "firmId": "firm-a", "role": "FIRM_ADMIN"})
token = t["token"]
q = urllib.parse.quote(target, safe="")

status, before = req(f"/api/coverage/files?path={q}", token=token)
check("the fixture is measured and uncovered", status == 200 and before["summary"]["pct"] < 90, str(before)[:200])

status, run = req("/api/coverage/runs", "POST", {"path": target, "pct": 90, "model": "glm-5.3:cloud"}, token)
check("a run starts on the test agent", status == 201, str(run)[:300])
if status != 201:
    sys.exit(1)

deadline = time.time() + 25 * 60
state = run["state"]
while state not in ("candidate", "accepted", "discarded", "completed_no_change", "failed", "canceled", "verification_failed") \
        and time.time() < deadline:
    time.sleep(10)
    _, detail = req(f"/api/coverage/runs/{run['id']}", token=token)
    state = detail["run"]["state"]
    print(f"  … {state} attempt {detail['run']['attempt']}/{detail['run']['maxAttempts']} at {detail['run']['lastPct']}", flush=True)
check("the run is verified and becomes a candidate", state == "candidate", f"ended {state}: {detail['run'].get('reason')}")
if state != "candidate":
    sys.exit(1)
check("the candidate is on its own branch", detail["run"]["branch"].startswith("test-agent/"))

status, decided = req(f"/api/coverage/runs/{run['id']}/accept", "POST", {}, token)
check("accept merges it into main", status == 200 and decided["run"]["state"] == "accepted", str(decided)[:300])

# The run as the Coverage screen's Activity sees it: its AG-UI stream, replayed now that the run is over.
def stream(path, token):
    r = urllib.request.Request(base + path, headers={"Accept": "text/event-stream", "Authorization": f"Bearer {token}"})
    events = []
    with urllib.request.urlopen(r, timeout=60) as resp:
        for raw in resp:
            line = raw.decode().rstrip("\n")
            if line.startswith("data: "):
                events.append(json.loads(line[len("data: "):]))
    return events

events = stream(f"/api/coverage/runs/{run['id']}/events", token)
names = [e.get("type") for e in events]
terminals = [n for n in names if n in ("RUN_FINISHED", "RUN_ERROR")]
check("the run's AG-UI stream starts once and ends once",
      names[:1] == ["RUN_STARTED"] and len(terminals) == 1 and names[-1] == terminals[0], f"{names[:3]} … {names[-3:]}")
check("it carries the agent's steps, tool calls, text and attempt results",
      {"STATE_SNAPSHOT", "STEP_STARTED", "TOOL_CALL_START", "TOOL_CALL_RESULT", "TEXT_MESSAGE_CONTENT"} <= set(names)
      and any(e.get("name") == "maf-lab/testgen-attempt" for e in events), str(sorted(set(names))))
print(f"  … {len(events)} AG-UI events; reasoning {'streamed' if 'REASONING_MESSAGE_CONTENT' in names else 'not returned by the provider'}", flush=True)

status, after = req(f"/api/coverage/files?path={q}", token=token)
check("the file's coverage is now at least its new threshold",
      status == 200 and after["summary"]["pct"] >= 90 and after["summary"]["threshold"] == 90, str(after.get("summary") if isinstance(after, dict) else after)[:200])

print(f"\n{'✗ ' + str(len(failures)) + ' check(s) failed' if failures else '✓ test generation end to end'}")
sys.exit(1 if failures else 0)
PY
