#!/usr/bin/env python3
"""Drive the A2A surface the way the specification describes it, knowing nothing about how it is served.

Discovery, a token, a question, a run that asks for what is missing, a dropped stream and a resubscription, a
push webhook, a cancellation, and a firm the partner may not see. Nothing here imports an A2A library: every
request is written from the 1.0 specification, so whatever passes is interoperable by construction.

    python3 scripts/a2a_probe.py [--base http://localhost:7171] [--client acme-portal] [--secret …]
"""

import argparse
import base64
import hashlib
import hmac
import json
import sys
import threading
import time
import urllib.error
import urllib.request
from http.server import BaseHTTPRequestHandler, HTTPServer

CARD_PATH = "/.well-known/agent-card.json"
failures = []


def report(ok, what, detail=""):
    print(f"{'  ok ' if ok else 'FAIL'}  {what}{(' — ' + detail) if detail else ''}")
    if not ok:
        failures.append(what)


def post(url, payload, token=None, stream=False):
    body = json.dumps(payload).encode()
    request = urllib.request.Request(url, data=body, headers={"Content-Type": "application/json"})
    if token:
        request.add_header("Authorization", f"Bearer {token}")
    response = urllib.request.urlopen(request, timeout=60)
    return response if stream else json.load(response)


def rpc(base, token, method, params, request_id=1):
    return post(f"{base}/a2a", {"jsonrpc": "2.0", "id": request_id, "method": method, "params": params}, token)


def result(answer, what):
    if "error" in answer:
        report(False, what, json.dumps(answer["error"]))
        return {}
    report(True, what)
    return answer.get("result", {})


def message(text, task_id=None):
    payload = {
        "kind": "message",
        "messageId": hashlib.sha1(f"{text}{time.time()}".encode()).hexdigest(),
        "role": "user",
        "parts": [{"kind": "text", "text": text}],
    }
    if task_id:
        payload["taskId"] = task_id
    return {"message": payload}


def events(base, token, method, params, stop_after=None):
    """Reads server-sent events, optionally dropping the connection early, as a real caller's would."""
    response = post(f"{base}/a2a", {"jsonrpc": "2.0", "id": 9, "method": method, "params": params}, token, stream=True)
    seen = []
    for raw in response:
        line = raw.decode().strip()
        if not line.startswith("data: "):
            continue
        seen.append(json.loads(line[6:]).get("result", {}))
        if stop_after and len(seen) >= stop_after:
            response.close()
            break
    return seen


class Webhook(BaseHTTPRequestHandler):
    received = []

    def do_POST(self):
        # A delivery may arrive chunked, so the body is read by length only when the sender gave one.
        length = int(self.headers.get("Content-Length") or 0)
        raw = self.rfile.read(length) if length else b""
        try:
            body = json.loads(raw) if raw else {}
        except ValueError:
            body = {}
        Webhook.received.append((self.headers.get("X-A2A-Notification-Token"), body))
        self.send_response(204)
        self.end_headers()

    def log_message(self, *args):
        pass


def verify_signature(card, key):
    """The documented procedure: drop `signatures`, sort every object's members, no whitespace, then HMAC-SHA256
    over protected + '.' + base64url(that)."""
    signatures = card.get("signatures") or []
    if not signatures:
        return False
    unsigned = {k: v for k, v in card.items() if k != "signatures"}
    canonical = json.dumps(unsigned, sort_keys=True, separators=(",", ":"), ensure_ascii=False)
    payload = base64.urlsafe_b64encode(canonical.encode()).rstrip(b"=")
    signing_input = signatures[0]["protected"].encode() + b"." + payload
    expected = base64.urlsafe_b64encode(hmac.new(key.encode(), signing_input, hashlib.sha256).digest()).rstrip(b"=")
    return hmac.compare_digest(expected.decode(), signatures[0]["signature"])


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--base", default="http://localhost:7171")
    parser.add_argument("--client", default="acme-portal")
    parser.add_argument("--secret", default="acme-portal-dev-secret")
    parser.add_argument("--signing-key", default="maf-lab-dev-signing-key-change-me-0123456789")
    parser.add_argument("--webhook-port", type=int, default=8899)
    args = parser.parse_args()

    # 1. Discovery — anonymous, as a stranger would.
    card = json.load(urllib.request.urlopen(f"{args.base}{CARD_PATH}", timeout=30))
    report("name" in card and "skills" in card, "the card is discoverable", card.get("name", ""))
    report("start_billing_run" not in json.dumps(card), "the public card hides the private skill")
    report(verify_signature(card, args.signing_key), "the card's signature verifies")
    endpoint = args.base

    # 2. A token, as the card's security scheme describes.
    refused = urllib.request.Request(f"{endpoint}/a2a", data=b"{}", headers={"Content-Type": "application/json"})
    try:
        urllib.request.urlopen(refused, timeout=30)
        report(False, "an anonymous protocol request is refused")
    except urllib.error.HTTPError as error:
        report(error.code == 401, "an anonymous protocol request is refused", f"HTTP {error.code}")

    token = post(f"{endpoint}/a2a/token", {"clientId": args.client, "clientSecret": args.secret})["accessToken"]
    report(bool(token), "a partner token is issued")

    extended = result(rpc(endpoint, token, "agent/getAuthenticatedExtendedCard", {}), "the extended card is served")
    report("start_billing_run" in json.dumps(extended), "the extended card adds the private skill")

    # 3. A question, answered with a message.
    answer = result(rpc(endpoint, token, "message/send", message("status of run 4417")), "a question is answered")
    text = "".join(part.get("text", "") for part in answer.get("parts", []))
    report(answer.get("kind") == "message" and answer.get("role") == "agent", "the answer is a specified message", text[:70])

    # 4. Work that takes time, asking for what the caller left out.
    asked = result(rpc(endpoint, token, "message/send", message("start a billing run")), "a run without a period is a task")
    task_id = asked.get("id", "")
    report(asked.get("status", {}).get("state") == "input-required", "the task asks for the period")

    # 5. A webhook for that task, then the answer that lets it finish.
    server = HTTPServer(("0.0.0.0", args.webhook_port), Webhook)
    threading.Thread(target=server.serve_forever, daemon=True).start()
    hook = f"http://host.docker.internal:{args.webhook_port}/hook"
    result(rpc(endpoint, token, "tasks/pushNotificationConfig/set",
               {"taskId": task_id, "pushNotificationConfig": {"url": hook, "token": "probe-token"}}),
           "a push configuration is registered")

    finished = result(rpc(endpoint, token, "message/send", message("2026-06", task_id)), "the task resumes")
    report(finished.get("status", {}).get("state") == "completed", "the resumed task completes")
    artifacts = finished.get("artifacts") or []
    report(any(a.get("name") == "billing-run-result" for a in artifacts), "the task carries its artifact")
    data = next((p.get("data") for a in artifacts for p in a.get("parts", []) if p.get("kind") == "data"), {})
    report(data.get("simulated") is True, "the artifact says the run was simulated")

    fetched = result(rpc(endpoint, token, "tasks/get", {"id": task_id}), "the task is fetchable afterwards")
    report(fetched.get("id") == task_id and fetched.get("kind") == "task", "the fetched task is the same task")

    time.sleep(1.5)
    report(len(Webhook.received) > 0, "the webhook received deliveries", f"{len(Webhook.received)} POST(s)")
    report(all(t == "probe-token" for t, _ in Webhook.received), "each delivery carried the registered token")
    server.shutdown()

    # 6. A stream, dropped, and picked up again.
    started = events(endpoint, token, "message/stream", message("start a billing run for firm-a 2026-07"), stop_after=2)
    streamed_id = started[0].get("id", "") if started else ""
    report(started and started[0].get("kind") == "task", "a streamed run starts with the task")
    resumed = events(endpoint, token, "tasks/resubscribe", {"id": streamed_id})
    report(resumed and resumed[0].get("kind") == "task", "resubscribing replays the whole task first")
    states = [e.get("status", {}).get("state") for e in started + resumed if "status" in e]
    report(states and states[-1] == "completed", "nothing was lost while the caller was away", " → ".join(states))

    # 7. Cancellation.
    running = result(rpc(endpoint, token, "message/send", message("start a billing run for firm-a 2026-08")),
                     "another run is started")
    cancelled = rpc(endpoint, token, "tasks/cancel", {"id": running.get("id", "")})
    report("error" in cancelled or cancelled.get("result", {}).get("status", {}).get("state") == "canceled",
           "a finished task cannot be cancelled, and says so",
           json.dumps(cancelled.get("error", cancelled.get("result", {}).get("status", {}))) [:70])

    # 8. A firm the partner may not see.
    refused_task = result(rpc(endpoint, token, "message/send", message("start a billing run for firm-b 2026-06")),
                          "a run for another firm is answered")
    report(refused_task.get("status", {}).get("state") == "rejected", "it is rejected")
    report("firm-b" not in json.dumps(refused_task), "and says nothing about that firm")

    print()
    if failures:
        print(f"{len(failures)} check(s) failed:")
        for failure in failures:
            print(f"  - {failure}")
        sys.exit(1)
    print("every check passed")


if __name__ == "__main__":
    main()
