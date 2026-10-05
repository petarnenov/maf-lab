#!/usr/bin/env bash
# Records runs from the running stack into evals/ui-events.jsonl — the AG-UI frames as they came off the wire, the
# way the browser receives them (through CopilotKit's runtime), and the run's trace as the trace API serves it, each
# with the state the browser should reach. A custom event on the wire fails the capture (agui-protocol-only). The web reducer is tested against these rather than against
# events a test author imagined, so re-run this whenever what the server emits changes.
#
# Usage: scripts/capture_ui_events.sh [base_url]   (default http://localhost:7171; the stack must be up)
set -euo pipefail
BASE="${1:-http://localhost:7171}"
OUT="$(dirname "$0")/../evals/ui-events.jsonl"
export BASE OUT

python3 - <<'PY'
import datetime, json, os, urllib.request, uuid

BASE, OUT = os.environ["BASE"], os.environ["OUT"]

def post(path, body, token=None):
    headers = {"content-type": "application/json"}
    if token:
        headers["authorization"] = f"Bearer {token}"
    request = urllib.request.Request(BASE + path, data=json.dumps(body).encode(), method="POST", headers=headers)
    with urllib.request.urlopen(request, timeout=300) as response:
        return response.read().decode()

def token(user, firm, role):
    return json.loads(post("/dev/token", {"userId": user, "tenantId": firm, "role": role}))["token"]

def run(access, message, thread=None):
    """One turn, as the browser makes it: through CopilotKit's runtime, the frames in the order they arrived."""
    run_id = "r_" + uuid.uuid4().hex[:12]
    body = {
        "threadId": thread or "c_" + uuid.uuid4().hex,
        "runId": run_id,
        "messages": [{"id": "u_" + uuid.uuid4().hex[:12], "role": "user", "content": message}],
        "tools": [], "context": [], "state": {}, "forwardedProps": {},
    }
    request = urllib.request.Request(BASE + "/copilotkit/agent/chat/run", data=json.dumps(body).encode(), method="POST",
                                     headers={"content-type": "application/json", "accept": "text/event-stream",
                                              "authorization": f"Bearer {access}"})
    frames = []
    with urllib.request.urlopen(request, timeout=300) as response:
        for raw in response:
            line = raw.decode().rstrip("\n")
            if line.startswith("data:"):
                data = json.loads(line[5:].strip())
                frames.append({"event": data["type"], "data": data})
    custom = [f for f in frames if f["event"] == "CUSTOM"]
    if custom:
        raise SystemExit(f"custom events on the wire: {[f['data'].get('name') for f in custom]}")
    trace = json.loads(urllib.request.urlopen(urllib.request.Request(
        BASE + f"/api/runs/{run_id}/trace", headers={"authorization": f"Bearer {access}"}), timeout=60).read())["events"]
    return frames, trace

def expected(frames, trace):
    """What the browser should end up showing, read off the frames and the trace themselves."""
    answer = "".join(f["data"].get("delta", "") for f in frames if f["event"] == "TEXT_MESSAGE_CONTENT")
    reasoning = "".join(f["data"].get("delta", "") for f in frames if f["event"] == "REASONING_MESSAGE_CONTENT")
    tools = [f["data"].get("toolCallName") for f in frames if f["event"] == "TOOL_CALL_START"]
    sources, pending = set(), None
    for frame in frames:
        if frame["event"] == "TOOL_CALL_RESULT":
            try:
                content = json.loads(frame["data"].get("content") or "{}")
            except ValueError:
                content = {}
            for source in (content.get("sources") or []) if isinstance(content, dict) else []:
                sources.add((source.get("docId"), source.get("sectionPath")))
        if frame["event"] == "RUN_FINISHED":
            interrupts = (frame["data"].get("outcome") or {}).get("interrupts") or []
            if interrupts:
                pending = {"id": interrupts[0].get("id"), "reason": interrupts[0].get("reason")}
    # Every frame gets a row in the monitor's AG-UI view, and the reasoning its own block in the chat.
    return {"answer": answer, "reasoning": reasoning, "toolCalls": tools, "sources": len(sources),
            "traceSteps": len(trace), "aguiFrames": len(frames), "pending": pending}

adam = token("adam", "firm-a", "USER")
rows = []
for name, what, message in [
    ("plain-answer", "an answer with no tool call", "Hello — what can you help me with?"),
    ("tool-call-with-sources", "a question answered from the documentation",
     "What is the procedure when a fee schedule is missing?"),
    ("pauses-for-confirmation", "a write that stops for the advisor's approval",
     "adjust the fee on A-1042 down by 200 because the client was overcharged in Q2"),
]:
    # When the run happened. A recording carries real timestamps — a proposal's expiry, above all — so replaying
    # it a week later would otherwise watch them all go stale. The test sets its clock to this.
    at = datetime.datetime.now(datetime.timezone.utc).isoformat()
    frames, trace = run(adam, message)
    rows.append({"id": name, "what": what, "capturedAt": at, "frames": frames, "trace": trace,
                 "expect": expected(frames, trace)})
    print(f"{name}: {len(frames)} frames, {len(rows[-1]['expect']['toolCalls'])} tool call(s), "
          f"{rows[-1]['expect']['sources']} source(s), {len(rows[-1]['expect']['reasoning'])} reasoning chars, "
          f"pending={rows[-1]['expect']['pending'] is not None}")

with open(OUT, "w", encoding="utf-8") as out:
    for row in rows:
        out.write(json.dumps(row, ensure_ascii=False) + "\n")
print(f"wrote {OUT}")
PY
