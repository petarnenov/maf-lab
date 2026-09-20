#!/usr/bin/env bash
# Records runs from the running stack into evals/ui-events.jsonl — the AG-UI frames as they came off the wire,
# each with the state the browser should reach. The web reducer is tested against these rather than against
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
    return json.loads(post("/dev/token", {"userId": user, "firmId": firm, "role": role}))["token"]

def run(access, message, thread=None):
    """One turn, as the browser makes it: the frames in the order they arrived."""
    body = {
        "threadId": thread,
        "runId": "r_" + uuid.uuid4().hex[:12],
        "messages": [{"id": "u_" + uuid.uuid4().hex[:12], "role": "user", "content": message}],
    }
    request = urllib.request.Request(BASE + "/api/chat", data=json.dumps(body).encode(), method="POST",
                                     headers={"content-type": "application/json", "authorization": f"Bearer {access}"})
    frames, name = [], None
    with urllib.request.urlopen(request, timeout=300) as response:
        for raw in response:
            line = raw.decode().rstrip("\n")
            if line.startswith("event:"):
                name = line[6:].strip()
            elif line.startswith("data:") and name:
                frames.append({"event": name, "data": json.loads(line[5:].strip())})
    return frames

def expected(frames):
    """What the browser should end up showing, read off the frames themselves."""
    answer = "".join(f["data"].get("delta", "") for f in frames if f["event"] == "TEXT_MESSAGE_CONTENT")
    tools = [f["data"].get("toolCallName") for f in frames if f["event"] == "TOOL_CALL_START"]
    sources, trace, pending = 0, 0, None
    for frame in frames:
        if frame["event"] == "CUSTOM":
            if frame["data"].get("name") == "maf-lab/sources":
                sources = len((frame["data"].get("value") or {}).get("sources") or [])
            elif frame["data"].get("name") == "maf-lab/trace":
                trace += 1
        if frame["event"] == "RUN_FINISHED":
            interrupts = (frame["data"].get("outcome") or {}).get("interrupts") or []
            if interrupts:
                pending = {"id": interrupts[0].get("id"), "reason": interrupts[0].get("reason")}
    return {"answer": answer, "toolCalls": tools, "sources": sources, "traceSteps": trace, "pending": pending}

adam = token("adam", "firm-a", "ADVISOR")
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
    frames = run(adam, message)
    rows.append({"id": name, "what": what, "capturedAt": at, "frames": frames, "expect": expected(frames)})
    print(f"{name}: {len(frames)} frames, {len(rows[-1]['expect']['toolCalls'])} tool call(s), "
          f"{rows[-1]['expect']['sources']} source(s), pending={rows[-1]['expect']['pending'] is not None}")

with open(OUT, "w", encoding="utf-8") as out:
    for row in rows:
        out.write(json.dumps(row, ensure_ascii=False) + "\n")
print(f"wrote {OUT}")
PY
