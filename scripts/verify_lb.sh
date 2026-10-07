#!/usr/bin/env bash
# Verifies the load-balanced stack end to end (openspec change add-load-balancer).
# Usage: scripts/verify_lb.sh [base_url]   (default http://localhost:7171; the stack must be up via compose)
set -euo pipefail
# compose mounts the repository at MAF_LAB_REPO (make exports it); outside make, it is this checkout.
export MAF_LAB_REPO="${MAF_LAB_REPO:-$(git -C "$(dirname "$0")/.." rev-parse --show-toplevel)}"
BASE="${1:-http://localhost:7171}"
export VERIFY_SCRIPT="$0"
COMPOSE="docker compose -f $(dirname "$0")/../compose/docker-compose.yml"
export BASE COMPOSE

python3 - <<'PY'
import json, os, signal, subprocess, sys, time, urllib.request, urllib.error, socket, collections, uuid

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

# Ctrl+C and SIGTERM (stop-anything): any job this started is cancelled, and a replica it stopped is started again by
# the `finally` below (sys.exit unwinds through it).
started_jobs = []

def stop(signum, frame):
    for job_id, who in started_jobs:
        try:
            req(f"/api/admin/jobs/{job_id}/cancel", "POST", token=who)
        except Exception:
            pass
    print("\n✗ Cancelled. Anything verification started was stopped; run `make verify` again.", flush=True)
    sys.exit(130)

signal.signal(signal.SIGINT, stop)
signal.signal(signal.SIGTERM, stop)

def token(user, firm, role):
    _, _, body = req("/dev/token", "POST", {"userId": user, "tenantId": firm, "role": role})
    return json.loads(body)["token"]

# A plugin's checks run only while the plugin is in use (introduce-plugins task 2.4): a part that is still core (no
# plugins/<name>/ folder yet) always runs; a plugin's part runs only when /api/plugins lists it.
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(os.environ.get("VERIFY_SCRIPT", "scripts/verify_lb.sh"))))
_answer = None

def in_use_answer():
    global _answer
    if _answer is None:
        _, _, body = req("/api/plugins", token=token("adam", "firm-a", "USER"))
        _answer = json.loads(body or "{}")
    return _answer

def plugin_in_use(name):
    if not os.path.isfile(os.path.join(ROOT, "plugins", name, "plugin.toml")):
        return True
    return name in {p["name"] for p in in_use_answer().get("plugins", [])}

# What a chat turn does depends on whether any domain is in use (introduce-plugins 7.1): `make core` has none, and a turn
# then declines with the fixed reply before any model or tool call.
def domain_in_use():
    return len(in_use_answer().get("domains", [])) > 0

# 4.1 entry point, routing, closed ports ---------------------------------------------------------------
status, _, body = req("/")
check("GET / serves the web app", status == 200 and 'id="root"' in body)
status, _, deep = req("/admin/feedback")
check("SPA deep link /admin/feedback returns index.html", status == 200 and 'id="root"' in deep)
status, _, body = req("/dev/users")
check("GET /dev/users through the balancer", status == 200 and "alice" in body)
status, _, body = req("/lb-health")
check("GET /lb-health", status == 200)
status, _, body = req("/copilotkit/info")
check("GET /copilotkit/info names the chat agent, and the test-run agent while coverage is in use (agui-protocol-only)",
      status == 200 and '"chat"' in body and ('"testgen"' in body or not plugin_in_use("coverage"))
      and '"telemetryDisabled":true' in body)
for port in (5080, 5090, 5174):
    s = socket.socket(); s.settimeout(2)
    refused = s.connect_ex(("127.0.0.1", port)) != 0
    s.close()
    check(f"host port {port} is closed", refused)

adam = token("adam", "firm-a", "USER")
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

# Billing's MCP server is a plugin (extract-billing): its checks run while it is in use.
if plugin_in_use("billing"):
    status, _, listing = mcp("tools/list", {}, adam)
    names = sorted(t["name"] for t in listing.get("result", {}).get("tools", []))
    check("MCP tools/list through /mcp",
          names == ["get_billing_run_status", "propose_fee_adjustment", "search_billing_runs", "search_documents",
                   "trace_billing_relationships"],
          str(names or listing))
    mcp_instances = collections.Counter()
    ok_calls = 0
    for _ in range(8):
        status, h, res = mcp("tools/call", {"name": "get_billing_run_status", "arguments": {"runId": "4417"}}, adam)
        mcp_instances[h.get("X-Instance")] += 1
        ok_calls += 1 if status == 200 and not res.get("result", {}).get("isError") else 0
    check("8 MCP calls succeed across >= 2 mcp replicas (no affinity)", ok_calls == 8 and len(mcp_instances) >= 2, f"ok={ok_calls} {dict(mcp_instances)}")

# 4.2 SSE through the balancer --------------------------------------------------------------------------
run_input = {
    "threadId": None,
    "runId": "r_verify_lb_" + uuid.uuid4().hex[:12],
    "messages": [{"id": "u_verify_lb", "role": "user", "content": "What is the procedure when a fee schedule is missing?"}],
}
r = urllib.request.Request(BASE + "/api/chat", data=json.dumps(run_input).encode(),
                           method="POST", headers={"content-type": "application/json", "authorization": f"Bearer {adam}"})
events = []
with urllib.request.urlopen(r, timeout=300) as resp:
    for raw in resp:
        line = raw.decode().rstrip("\n")
        # An AG-UI event names itself in its payload (the official server leaves the SSE event name out).
        if line.startswith("data:"):
            events.append((json.loads(line[5:].strip())["type"], time.monotonic()))
order = [e for e, _ in events]
dedup = [e for i, e in enumerate(order) if i == 0 or e != order[i - 1]]
check("only the protocol's own events, never a custom one (agui-protocol-only)", "CUSTOM" not in order, " ".join(sorted(set(order))))
if domain_in_use():
    check("a run starts, calls a tool, answers and finishes, in that order",
          order[0] == "RUN_STARTED" and order[-1] == "RUN_FINISHED"
          and order.index("TOOL_CALL_START") < order.index("TOOL_CALL_RESULT")
          and order.index("TOOL_CALL_RESULT") < order.index("TEXT_MESSAGE_CONTENT")
          and order.count("RUN_STARTED") == 1 and order.count("RUN_FINISHED") == 1, " ".join(dedup))
    check("the run's events arrive incrementally (not buffered)", events[-1][1] - events[0][1] > 0.2,
          f"first→last {events[-1][1] - events[0][1]:.2f}s over {len(events)} events")
else:
    # No domain in use: the fixed reply, with no tool call (make core-turn-check proves no model or Jev call either).
    check("with no domain in use, a run starts, declines without a tool and finishes",
          order[0] == "RUN_STARTED" and order[-1] == "RUN_FINISHED" and "TEXT_MESSAGE_CONTENT" in order
          and not any(t.startswith("TOOL_CALL_") for t in order)
          and order.count("RUN_STARTED") == 1 and order.count("RUN_FINISHED") == 1, " ".join(dedup))

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
    # A restarted replica may come back on a new address; the balancer resolves upstreams only when it reloads (see
    # nginx.conf), so reload it as `make up` does, or later checks would see one replica.
    subprocess.run(f"{COMPOSE} exec -T lb nginx -c /etc/nginx/lb/nginx.conf -s reload", shell=True, capture_output=True)

# 4.4 admin jobs across replicas ------------------------------------------------------------------------
alice = token("alice", "firm-a", "TENANT_ADMIN")
status, _, body = req("/api/admin/index/run", "POST", token=alice)
job = json.loads(body)
started_jobs.append((job["jobId"], alice))
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
# Without the vector store (the qdrant plugin) an index run cannot reach a collection and fails: it still finishes, and
# that is what the core alone can show (a clean refusal is extract-index-admin's).
if plugin_in_use("qdrant"):
    check("job finishes succeeded", state == "succeeded", state)
else:
    check("job finishes (failed without a vector store)", state in ("succeeded", "failed"), state)
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

    # Both api replicas back in the balancer's rotation first: the replica-stop section above leaves the restarted one out
    # for nginx's fail_timeout after its first failed attempt, so wait on the effect (both answering), not on a clock.
    answering, rotation_deadline = set(), time.time() + 30
    while len(answering) < 2 and time.time() < rotation_deadline:
        _, h, _ = req("/api/me", token=adam)
        answering.add(h.get("X-Instance"))
        time.sleep(0.2)
    check("both api replicas answer through the balancer again", len(answering) >= 2, str(sorted(i for i in answering if i)))

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
if plugin_in_use("compliance"):
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

# 4.6 a write proposed on one replica and confirmed through the balancer --------------------------------
# The MCP server keeps nothing between the two calls, so whichever replica answers must honour the proposal.
# The run undoes its own write (repeatable-verify): an increase first, which the sign guard always allows, then the
# matching reduction, so the fee ends where it started and the next run finds the same data.
def propose(amount, reason, request_state=None, approve=None):
    params = {"name": "propose_fee_adjustment",
              "arguments": {"accountId": "A-1042", "amount": amount, "reason": reason}}
    if request_state:
        params["requestState"] = request_state
    if approve is not None:
        params["inputResponses"] = {"confirmation": {"action": "accept" if approve else "decline",
                                                     "content": {"approve": True} if approve else None}}
    _, _, body = mcp("tools/call", params, adam)
    return body

def summary_of(result):
    return next((r.get("params", {}).get("_meta", {}).get("maf-lab/write-summary", {})
                 for r in (result.get("inputRequests") or {}).values()), {})

if plugin_in_use("billing"):
    asked = propose(200, "verify_lb end-to-end check").get("result", {})
    state = asked.get("requestState")
    check("a proposal asks for input and writes nothing", bool(state) and bool(asked.get("inputRequests")),
          json.dumps(asked)[:140])

    if state:
        summary = summary_of(asked)
        before = summary.get("currentFee")
        check("the proposal names the account, its fee and what it would become",
              summary.get("accountId") == "A-1042" and "currentFee" in summary and "resultingFee" in summary,
              json.dumps(summary)[:140])

        first = propose(200, "verify_lb end-to-end check", state, approve=True).get("result", {}).get("structuredContent", {})
        check("confirming through the balancer applies it", first.get("status") == "applied",
              json.dumps(first)[:140])

        second = propose(200, "verify_lb end-to-end check", state, approve=True).get("result", {}).get("structuredContent", {})
        check("confirming the same proposal twice applies it once",
              second.get("status") == "already_applied"
              and second.get("adjustment", {}).get("currentFee") == first.get("adjustment", {}).get("currentFee"),
              json.dumps(second)[:140])

        undo = propose(-200, "verify_lb reversal of its own check").get("result", {})
        undone = propose(-200, "verify_lb reversal of its own check", undo.get("requestState"), approve=True) \
            .get("result", {}).get("structuredContent", {}) if undo.get("requestState") else {}
        after = undone.get("adjustment", {}).get("currentFee")
        check("the run undoes its own adjustment: the fee is back where it started",
              undone.get("status") == "applied" and before is not None and after is not None
              and abs(float(after) - float(before)) < 0.005,
              f"before={before} after={after} {json.dumps(undone)[:100]}")

print()
print("ALL CHECKS PASSED" if not failures else f"{len(failures)} CHECK(S) FAILED: {failures}")
sys.exit(1 if failures else 0)
PY
