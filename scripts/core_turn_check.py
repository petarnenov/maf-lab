#!/usr/bin/env python3
"""make core-turn-check (introduce-plugins 7.1): on a core-only stack (`make core`: no plugin, no built-in domain), a
turn declines with the fixed reply before any model, decision-engine or tool call.

It asks /api/plugins first (no plugin and no domain must be in use, or the stack is not the core alone), then sends
one English and one Cyrillic question through /api/chat. The stub's request journal (WireMock's GET/DELETE
/__admin/requests) on both Ollama instances is reset just before each turn and read just after: it must stay empty.

Progress: one line per step with its elapsed time, then a PASS or FAIL line per check (CI is not a terminal).
Stopping: Ctrl+C or SIGTERM closes the run's request, which stops the run on the server (stop-anything), and exits 130.
"""
from __future__ import annotations

import json
import signal
import sys
import time
import urllib.error
import urllib.request
import uuid

BASE = sys.argv[1] if len(sys.argv) > 1 else "http://localhost:7171"
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


def turn(question: str, token: str) -> list[dict]:
    run = {"threadId": None, "runId": "r_core_" + uuid.uuid4().hex[:12],
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
    check("no plugin is in use", in_use.get("plugins") == [], json.dumps([p.get("name") for p in in_use.get("plugins", [])]))
    check("no domain is in use", in_use.get("domains") == [], json.dumps(in_use.get("domains")))
    if failures:
        return 1

    for question, reply in QUESTIONS:
        step(f"ask {question!r}")
        journal("DELETE")
        events = turn(question, token)
        calls = journal()
        types = [e["type"] for e in events]
        text = "".join(e.get("delta", "") for e in events if e["type"] == "TEXT_MESSAGE_CONTENT")
        check("the run starts once and ends finished", types.count("RUN_STARTED") == 1 and types[-1:] == ["RUN_FINISHED"],
              " ".join(dict.fromkeys(types)))
        check("the answer is the fixed reply, exactly", text == reply, text[:120])
        check("no tool call and no step", not any(t.startswith(("TOOL_CALL_", "STEP_")) for t in types))
        check("no model, embedding or decision-engine call reached either Ollama instance", calls == [],
              ", ".join(f"{c['method']} {c['path']}" for c in calls))

    step("done")
    return 1 if failures else 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except urllib.error.URLError as e:
        print(f"FAIL  the stack did not answer: {e.reason}")
        sys.exit(1)
