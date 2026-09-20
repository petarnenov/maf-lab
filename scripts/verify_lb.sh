#!/usr/bin/env bash
# Verifies the load-balanced stack end to end (openspec change add-load-balancer).
# Usage: scripts/verify_lb.sh [base_url]   (default http://localhost:7171; the stack must be up via compose)
set -euo pipefail
BASE="${1:-http://localhost:7171}"
COMPOSE="docker compose -f $(dirname "$0")/../compose/docker-compose.yml"
export BASE COMPOSE

python3 - <<'PY'
import json, os, subprocess, sys, time, urllib.request, urllib.error, socket, collections, uuid

BASE = os.environ["BASE"]
COMPOSE = os.environ["COMPOSE"]
failures = []

A2A_SECRET = os.environ.get("A2A_PARTNER_SECRET", "acme-portal-dev-secret")
COMPLIANCE_SECRET = os.environ.get("COMPLIANCE_CLIENT_SECRET", "assistant-dev-secret")

def check(name, ok, detail=""):
    print(f"{'PASS' if ok else 'FAIL'}  {name}{('  — ' + detail) if detail else ''}")
    if not ok:
        failures.append(name)

def req(path, method="GET", body=None, token=None, headers=None, timeout=30):
    h = {"content-type": "application/json", **(headers or {})}
    if token:
        h["authorization"] = f"Bearer {token}"
    data = json.dumps(body).encode() if body is not None else None
    r = urllib.request.Request(BASE + path, data=data, method=method, headers=h)
    try:
        with urllib.request.urlopen(r, timeout=timeout) as resp:
            return resp.status, dict(resp.headers), resp.read().decode()
    except urllib.error.HTTPError as e:
        return e.code, dict(e.headers), e.read().decode()

def token(user, firm, role):
    _, _, body = req("/dev/token", "POST", {"userId": user, "firmId": firm, "role": role})
    return json.loads(body)["token"]

# 4.1 entry point, routing, closed ports ---------------------------------------------------------------
status, _, body = req("/")
check("GET / serves the web app", status == 200 and 'id="root"' in body)
status, _, deep = req("/admin/feedback")
check("SPA deep link /admin/feedback returns index.html", status == 200 and 'id="root"' in deep)
status, _, body = req("/dev/users")
check("GET /dev/users through the balancer", status == 200 and "alice" in body)
status, _, body = req("/lb-health")
check("GET /lb-health", status == 200)
for port in (5080, 5090, 5174):
    s = socket.socket(); s.settimeout(2)
    refused = s.connect_ex(("127.0.0.1", port)) != 0
    s.close()
    check(f"host port {port} is closed", refused)

adam = token("adam", "firm-a", "ADVISOR")
instances = collections.Counter()
for _ in range(20):
    status, headers, _ = req("/api/me", token=adam)
    instances[headers.get("X-Instance")] += 1
check("20 x /api/me spread over >= 2 api replicas", len(instances) >= 2, dict(instances).__str__())

# MCP through the balancer (protocol 2026-07-28, stateless)
def mcp(method, params, tok):
    meta = {"io.modelcontextprotocol/protocolVersion": "2026-07-28",
            "io.modelcontextprotocol/clientCapabilities": {},
            "io.modelcontextprotocol/clientInfo": {"name": "verify_lb", "version": "1.0"}}
    headers = {"accept": "application/json, text/event-stream", "mcp-protocol-version": "2026-07-28", "mcp-method": method}
    if method == "tools/call":
        headers["mcp-name"] = params["name"]
    status, h, body = req("/mcp", "POST", {"jsonrpc": "2.0", "id": 1, "method": method, "params": {**params, "_meta": meta}}, tok, headers)
    if body.startswith("event:") or body.startswith("data:"):
        body = next(l[5:].strip() for l in body.splitlines() if l.startswith("data:"))
    return status, h, json.loads(body) if body else {}

status, _, listing = mcp("tools/list", {}, adam)
names = sorted(t["name"] for t in listing.get("result", {}).get("tools", []))
check("MCP tools/list through /mcp", names == ["get_billing_run_status", "search_billing_runs", "search_documents"], str(names or listing))
mcp_instances = collections.Counter()
ok_calls = 0
for _ in range(8):
    status, h, res = mcp("tools/call", {"name": "get_billing_run_status", "arguments": {"runId": "4417"}}, adam)
    mcp_instances[h.get("X-Instance")] += 1
    ok_calls += 1 if status == 200 and not res.get("result", {}).get("isError") else 0
check("8 MCP calls succeed across >= 2 mcp replicas (no affinity)", ok_calls == 8 and len(mcp_instances) >= 2, f"ok={ok_calls} {dict(mcp_instances)}")

# 4.2 SSE through the balancer --------------------------------------------------------------------------
r = urllib.request.Request(BASE + "/api/chat", data=json.dumps({"message": "What is the procedure when a fee schedule is missing?"}).encode(),
                           method="POST", headers={"content-type": "application/json", "authorization": f"Bearer {adam}"})
events, name = [], None
with urllib.request.urlopen(r, timeout=300) as resp:
    for raw in resp:
        line = raw.decode().rstrip("\n")
        if line.startswith("event:"):
            name = line[6:].strip()
            events.append((name, time.monotonic()))
order = [e for e, _ in events]
dedup = [e for i, e in enumerate(order) if i == 0 or e != order[i - 1]]
check("SSE order: tool_call_started → tool_call_finished → … → sources → done",
      "tool_call_started" in order and order.index("tool_call_started") < order.index("tool_call_finished")
      and order.index("sources") < order.index("done") and order[-1] == "done", " ".join(dedup))
check("SSE events arrive incrementally (not buffered)", events[-1][1] - events[0][1] > 0.2,
      f"first→last {events[-1][1] - events[0][1]:.2f}s over {len(events)} events")

# 4.3 replica failure -----------------------------------------------------------------------------------
api_containers = subprocess.run(f"{COMPOSE} ps -q api", shell=True, capture_output=True, text=True).stdout.split()
check("api runs >= 2 replicas", len(api_containers) >= 2, str(len(api_containers)))
victim = api_containers[0]
subprocess.run(["docker", "stop", victim], capture_output=True)
try:
    oks = sum(1 for _ in range(10) if req("/api/me", token=adam)[0] == 200)
    check("with one api replica stopped, 10/10 requests still succeed", oks == 10, f"{oks}/10")
finally:
    subprocess.run(["docker", "start", victim], capture_output=True)
    for _ in range(60):
        health = subprocess.run(["docker", "inspect", "-f", "{{.State.Health.Status}}", victim], capture_output=True, text=True).stdout.strip()
        if health == "healthy":
            break
        time.sleep(2)

# 4.4 admin jobs across replicas ------------------------------------------------------------------------
alice = token("alice", "firm-a", "FIRM_ADMIN")
status, _, body = req("/api/admin/index/run", "POST", token=alice)
job = json.loads(body)
status2, _, body2 = req("/api/admin/index/run", "POST", token=alice)
second = json.loads(body2)
# A different id is only correct if the first job had already finished (dedup applies to *running* jobs).
_, _, first_now = req(f"/api/admin/jobs/{job['jobId']}", token=alice)
first_done = json.loads(first_now)["state"] in ("succeeded", "failed")
check("second start while running returns the same job", status == 202 and (second["jobId"] == job["jobId"] or first_done),
      f"{job['jobId']} vs {second['jobId']} (first finished: {first_done})")
seen, state, polls = collections.Counter(), job["state"], 0
deadline = time.time() + 900
# Keep polling for at least 8 reads so the "any replica can answer" check does not depend on how fast the job is.
while (state not in ("succeeded", "failed") or polls < 8) and time.time() < deadline:
    polls += 1
    s, h, b = req(f"/api/admin/jobs/{job['jobId']}", token=alice)
    if s != 200:
        check("job status readable on every poll", False, f"HTTP {s} from {h.get('X-Instance')}")
        break
    seen[h.get("X-Instance")] += 1
    state = json.loads(b)["state"]
    time.sleep(0.3)
check("job finishes succeeded", state == "succeeded", state)
check("job status was served by >= 2 replicas", len(seen) >= 2, str(dict(seen)))

# 4.5 the A2A surface through the balancer ------------------------------------------------------------
status, _, card = req("/.well-known/agent-card.json")
check("agent card is served anonymously through the balancer",
      status == 200 and "skills" in card and "start_billing_run" not in card)
status, _, _ = req("/a2a", "POST", {"jsonrpc": "2.0", "id": 1, "method": "message/send"})
check("the protocol endpoint refuses an anonymous caller", status == 401, f"HTTP {status}")

status, _, body = req("/a2a/token", "POST", {"clientId": "acme-portal", "clientSecret": A2A_SECRET})
partner = json.loads(body)["accessToken"] if status == 200 else ""
check("a partner token is issued through the balancer", status == 200 and bool(partner), f"HTTP {status}")

def a2a(method, params, timeout=90):
    _, headers, raw = req("/a2a", "POST", {"jsonrpc": "2.0", "id": 1, "method": method, "params": params},
                          token=partner, timeout=timeout)
    return json.loads(raw), headers.get("X-Instance")

def user_message(text, task=None):
    message = {"kind": "message", "messageId": uuid.uuid4().hex, "role": "user",
               "parts": [{"kind": "text", "text": text}]}
    if task:
        message["taskId"] = task
    return {"message": message}

if partner:
    answer, _ = a2a("message/send", user_message("status of run 4417"))
    check("a partner question is answered in the 1.0 shape",
          answer.get("result", {}).get("kind") == "message" and answer["result"].get("role") == "agent",
          json.dumps(answer)[:90])

    started, first_instance = a2a("message/send", user_message("start a billing run for firm-a 2026-06"))
    task = started.get("result", {})
    check("a run through the balancer completes as a task",
          task.get("kind") == "task" and task.get("status", {}).get("state") == "completed",
          json.dumps(task.get("status", {}))[:90])

    # The task lives in the shared store, so it can be read back through a different replica.
    instances = set()
    fetched = {}
    for _ in range(8):
        fetched, instance = a2a("tasks/get", {"id": task.get("id", "")})
        instances.add(instance)
    check("the task reads the same through every replica",
          fetched.get("result", {}).get("id") == task.get("id"), str(sorted(i for i in instances if i)))
    check("more than one replica answered for the task", len(instances) >= 2, str(sorted(i for i in instances if i)))

# 4.6 the second agent, through the same entry point --------------------------------------------------
status, _, card = req("/compliance/.well-known/agent-card.json")
check("the compliance agent's card is served through the balancer",
      status == 200 and "review_fee_adjustment" in card and "maf-lab compliance reviewer" in card)

status, _, body = req("/compliance/a2a/token", "POST",
                      {"clientId": "maf-lab-assistant", "clientSecret": COMPLIANCE_SECRET})
reviewer = json.loads(body)["accessToken"] if status == 200 else ""
check("the assistant's credentials are accepted by the reviewer", status == 200 and bool(reviewer), f"HTTP {status}")

if reviewer:
    # A token for the assistant's own surface must not open this one: different agent, different audience.
    _, _, crossed = req("/compliance/a2a", "POST",
                        {"jsonrpc": "2.0", "id": 1, "method": "message/send", "params": user_message("hello")},
                        token=partner)
    check("a token for the billing agent does not open the compliance agent", "error" in crossed or not crossed,
          crossed[:60])

    adjustment = {"adjustmentId": "ADJ-LB", "firmId": "firm-a", "accountId": "ACC-1042",
                  "amount": 250, "reason": "Overcharged in Q2"}
    review = {"message": {"kind": "message", "messageId": uuid.uuid4().hex, "role": "user",
                          "parts": [{"kind": "data", "data": adjustment}]}}
    _, headers, raw = req("/compliance/a2a", "POST",
                          {"jsonrpc": "2.0", "id": 1, "method": "message/send", "params": review},
                          token=reviewer, timeout=180)
    answer = json.loads(raw)
    task = answer.get("result", {})
    state = task.get("status", {}).get("state")
    check("a review runs end to end through the balancer", state in ("completed", "input-required"),
          json.dumps(answer)[:90])
    if state == "completed":
        verdict = next((p.get("data") for a in task.get("artifacts", []) for p in a.get("parts", [])
                        if p.get("kind") == "data"), {})
        check("the verdict is structured and says it is simulated",
              verdict.get("decision") in ("approved", "refused") and verdict.get("simulated") is True,
              json.dumps(verdict)[:80])

    replicas = set()
    for _ in range(8):
        _, headers, _ = req("/compliance/.well-known/agent-card.json")
        replicas.add(headers.get("X-Instance"))
    check("the compliance tier answers from more than one replica", len(replicas) >= 2,
          str(sorted(r for r in replicas if r)))

print()
print("ALL CHECKS PASSED" if not failures else f"{len(failures)} CHECK(S) FAILED: {failures}")
sys.exit(1 if failures else 0)
PY
