#!/usr/bin/env python3
"""make core-turn-check (introduce-plugins 7.1): on a core-only stack (`make core`: no plugin, so no domain), a
turn declines with the fixed reply before any model, decision-engine or tool call.

It asks /api/plugins first (no plugin and no domain must be in use, or the stack is not the core alone), checks that the
balancer answers a plugin's route shape with 404 rather than the web app, then sends one English and one Cyrillic
question through /api/chat. The stub's request journal (WireMock's GET/DELETE /__admin/requests) on both Ollama
instances is reset just before each turn and read just after: it must stay empty. After each turn, the shared store
must hold no live trace for it (no monitor, no observer).

Progress: one line per step with its elapsed time, then a PASS or FAIL line per check (CI is not a terminal).
Stopping: Ctrl+C or SIGTERM closes the run's request, which stops the run on the server (stop-anything), and exits 130.
"""
from __future__ import annotations

import json
import os
import signal
import subprocess
import sys
import time
import urllib.error
import urllib.request
import uuid

BASE = sys.argv[1] if len(sys.argv) > 1 else "http://localhost:7171"
# The shared store is reached as verify_lb.sh reaches the stack: through compose, in the project make exports.
COMPOSE = ["docker", "compose", "-p", os.environ.get("COMPOSE_PROJECT_NAME", "maf-lab"), "-f",
           os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "compose", "docker-compose.yml")]
# Both Ollama instances, as the host reaches them: the query one and the batch one.
STUBS = ("http://127.0.0.1:11435", "http://127.0.0.1:11436")
# The oracle: NoDomain's fixed replies (src/Maf.Lab.Api/Agent/IntentClassifier.cs), copied, not read from the api.
REPLY_EN = "No domain is enabled for this assistant yet, so it cannot answer questions. Your administrator can enable one."
REPLY_BG = ("За този асистент още няма включена област, затова той не може да отговаря на въпроси. "
            "Администраторът Ви може да включи такава.")
QUESTIONS = (("What is the procedure when a fee schedule is missing?", REPLY_EN),
             ("Каква е процедурата, когато липсва тарифа?", REPLY_BG))

started = time.monotonic()
failures: list[str] = []
in_flight: list = []


def step(text: str) -> None:
    print(f"▸ {text}  ({time.monotonic() - started:.1f}s)", flush=True)


def check(name: str, ok: bool, detail: str = "") -> None:
    print(f"{'PASS' if ok else 'FAIL'}  {name}{('  — ' + detail) if detail else ''}", flush=True)
    if not ok:
        failures.append(name)


def stop(signum, frame):
    for response in in_flight:
        try:
            response.close()
        except Exception:
            pass
    print("\n✗ Cancelled. The run's request was closed, which stops it on the server; run it again.", flush=True)
    sys.exit(130)


def call(url: str, method: str = "GET", body: dict | None = None, token: str | None = None, timeout: int = 60):
    headers = {"content-type": "application/json"}
    if token:
        headers["authorization"] = f"Bearer {token}"
    data = json.dumps(body).encode() if body is not None else None
    return urllib.request.urlopen(urllib.request.Request(url, data=data, method=method, headers=headers), timeout=timeout)


def journal(method: str = "GET") -> list[dict]:
    seen = []
    for stub in STUBS:
        with call(stub + "/__admin/requests", method) as r:
            seen += [{**req, "stub": stub} for req in json.loads(r.read())["requests"]]
    return seen


def live_trace_kept(run_id: str) -> bool | None:
    """Whether the shared store holds a live trace for the run (the monitor's `runtrace:<runId>` list); None when the
    store cannot be asked."""
    result = subprocess.run([*COMPOSE, "exec", "-T", "redis", "redis-cli", "EXISTS", f"runtrace:{run_id}"],
                            capture_output=True, text=True, timeout=30)
    return None if result.returncode != 0 else result.stdout.strip() != "0"


def live_traces() -> set[str] | None:
    """Every live trace the shared store holds; None when it cannot be asked. Compared before and after the turns, so an
    older run's trace (kept for the run grace period) is not mistaken for one of these."""
    result = subprocess.run([*COMPOSE, "exec", "-T", "redis", "redis-cli", "--scan", "--pattern", "runtrace:*"],
                            capture_output=True, text=True, timeout=30)
    return None if result.returncode != 0 else {k for k in result.stdout.split() if k}


def turn(question: str, token: str, run_id: str) -> list[dict]:
    run = {"threadId": None, "runId": run_id,
           "messages": [{"id": "u_core", "role": "user", "content": question}]}
    events = []
    response = call(BASE + "/api/chat", "POST", run, token, timeout=120)
    in_flight.append(response)
    try:
        for raw in response:
            line = raw.decode().rstrip("\n")
            if line.startswith("data:"):
                events.append(json.loads(line[5:].strip()))
    finally:
        in_flight.remove(response)
        response.close()
    return events


def main() -> int:
    signal.signal(signal.SIGINT, stop)
    signal.signal(signal.SIGTERM, stop)

    step("sign in and ask what is in use")
    with call(BASE + "/dev/token", "POST", {"userId": "adam", "tenantId": "firm-a", "role": "USER"}) as r:
        token = json.loads(r.read())["token"]
    with call(BASE + "/api/plugins", token=token) as r:
        in_use = json.loads(r.read())
    # First, so a stack that is not the core alone is named rather than failing later for another reason.
    # The core's minimum providers (introduce-provider-plugins 5x: MAF_CORE_PROVIDERS) are always installed; nothing else.
    others = [p.get("name") for p in in_use.get("plugins", []) if p.get("kind") != "provider"]
    check("no plugin but the core's providers is in use", others == [], json.dumps([p.get("name") for p in in_use.get("plugins", [])]))
    check("no domain is in use", in_use.get("domains") == [], json.dumps(in_use.get("domains")))
    if failures:
        return 1

    # The balancer reserves the plugins' route shapes (introduce-plugins 2.1): with none installed, an MCP or A2A path
    # answers 404 from the balancer itself, never the web app's page.
    step("ask the balancer for plugin route shapes")
    for path in ("/code/mcp", "/a-plugin-that-is-not-installed/mcp", "/a-plugin-that-is-not-installed/a2a"):
        try:
            with call(BASE + path, "POST", {}) as r:
                status, body = r.status, r.read().decode(errors="replace")
        except urllib.error.HTTPError as e:
            status, body = e.code, e.read().decode(errors="replace")
        check(f"{path} answers 404, not the web app", status == 404 and 'id="root"' not in body, f"HTTP {status}")

    traces_before = live_traces()
    for question, reply in QUESTIONS:
        step(f"ask {question!r}")
        journal("DELETE")
        run_id = "r_core_" + uuid.uuid4().hex[:12]
        events = turn(question, token, run_id)
        calls = journal()
        kept = live_trace_kept(run_id)
        types = [e["type"] for e in events]
        text = "".join(e.get("delta", "") for e in events if e["type"] == "TEXT_MESSAGE_CONTENT")
        check("the run starts once and ends finished", types.count("RUN_STARTED") == 1 and types[-1:] == ["RUN_FINISHED"],
              " ".join(dict.fromkeys(types)))
        check("the answer is the fixed reply, exactly", text == reply, text[:120])
        check("no tool call and no step", not any(t.startswith(("TOOL_CALL_", "STEP_")) for t in types))
        check("no model, embedding or decision-engine call reached either Ollama instance", calls == [],
              ", ".join(f"{c['method']} {c['path']}" for c in calls))
        # With no monitor, a turn is observed by nobody: no live trace is written (introduce-plugins 5.3).
        check("no live trace was written to the shared store", kept is False,
              "the store could not be asked" if kept is None else f"runtrace:{run_id}")

    traces_after = live_traces()
    written = None if traces_before is None or traces_after is None else traces_after - traces_before
    check("no live trace appeared in the shared store under any id", written == set(),
          "the store could not be asked" if written is None else ", ".join(sorted(written)))

    step("done")
    return 1 if failures else 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except urllib.error.URLError as e:
        print(f"FAIL  the stack did not answer: {e.reason}")
        sys.exit(1)
