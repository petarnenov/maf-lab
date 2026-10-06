"""The sample plugin's end-to-end check (introduce-plugins 6.1), run by `make verify` while the plugin is in use: one
question routed to its domain, answered through its MCP server behind the balancer. Skips itself when /api/plugins
does not list the plugin, so `make verify` on a stack without it stays green.

Progress: one line per step, as `make verify` prints its checks. Stopping: Ctrl+C or SIGTERM closes the run's request,
which cancels the run on the server (stop-anything), and exits 130.
"""
from __future__ import annotations

import json
import signal
import sys
import urllib.error
import urllib.request
import uuid

BASE = sys.argv[1] if len(sys.argv) > 1 else "http://localhost:7171"
NAME = "_example"
TOOL = "get_example_fact"
QUESTION = "What is the example fact for this tenant?"


def stop(signum, frame):
    print("\n✗ Cancelled. The run's request was closed, which stops it on the server; run `make verify` again.", flush=True)
    sys.exit(130)


def request(path: str, body: dict | None = None, token: str | None = None) -> urllib.request.Request:
    headers = {"content-type": "application/json"}
    if token:
        headers["authorization"] = f"Bearer {token}"
    data = json.dumps(body).encode() if body is not None else None
    return urllib.request.Request(BASE + path, data=data, method="POST" if body is not None else "GET", headers=headers)


def main() -> int:
    signal.signal(signal.SIGINT, stop)
    signal.signal(signal.SIGTERM, stop)
    with urllib.request.urlopen(request("/dev/token", {"userId": "adam", "tenantId": "firm-a", "role": "USER"}), timeout=30) as r:
        token = json.loads(r.read())["token"]
    with urllib.request.urlopen(request("/api/plugins", token=token), timeout=30) as r:
        if NAME not in {p["name"] for p in json.loads(r.read()).get("plugins", [])}:
            print(f"SKIP  {NAME} is not in use")
            return 0

    print(f"…     asking {QUESTION!r}", flush=True)
    run = {"threadId": None, "runId": "r_e2e_" + uuid.uuid4().hex[:12],
           "messages": [{"id": "u_e2e", "role": "user", "content": QUESTION}]}
    events = []
    with urllib.request.urlopen(request("/api/chat", run, token), timeout=300) as resp:
        for raw in resp:
            line = raw.decode().rstrip("\n")
            if line.startswith("data:"):
                events.append(json.loads(line[5:].strip()))
    types = [e["type"] for e in events]
    called = [e for e in events if e["type"] == "TOOL_CALL_START" and e.get("toolCallName") == TOOL]
    results = {e.get("toolCallId") for e in events if e["type"] == "TOOL_CALL_RESULT"}
    checks = [
        (f"the question reached {TOOL}", bool(called)),
        (f"{TOOL} answered", any(c.get("toolCallId") in results for c in called)),
        ("the run answered and finished", "TEXT_MESSAGE_CONTENT" in types and types[-1:] == ["RUN_FINISHED"]),
    ]
    for name, ok in checks:
        print(f"{'PASS' if ok else 'FAIL'}  {name}")
    return 0 if all(ok for _, ok in checks) else 1


if __name__ == "__main__":
    try:
        sys.exit(main())
    except urllib.error.URLError as e:
        print(f"FAIL  the stack did not answer: {e.reason}")
        sys.exit(1)
