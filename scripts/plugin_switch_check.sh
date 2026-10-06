#!/usr/bin/env bash
# make plugin-switch-check (introduce-plugins 5.2, spec plugins "A remote plugin is switched off"): on the running dev
# stack, `make plugin-off NAME=code` and then `make plugin-on NAME=code`, checking what the scenario promises:
#   - a run in flight when it switches off ends finished, not cut off;
#   - the api answers through the balancer the whole time (its replicas restart one at a time);
#   - off: no mcp-code container, /code/mcp is 404 (never the web app), and no api replica offers the codebase domain;
#   - lb and copilot-runtime are untouched (same container, same start); each api replica is the same container,
#     restarted;
#   - on: the inverse, with lb and copilot-runtime still untouched.
#
# Progress: one line per step, then a PASS or FAIL line per check. Stopping: Ctrl+C or SIGTERM switches the plugin back
# on (the stack ends as it began) and exits 130.
set -uo pipefail
cd "$(dirname "$0")/.."
BASE_URL="${BASE_URL:-http://localhost:7171}"
export COMPOSE_PROJECT_NAME="${COMPOSE_PROJECT_NAME:-maf-lab}"
NAME=code
WORK="$(mktemp -d)"
FAILURES=0

step() { echo "▸ $*"; }
check() { if [[ "$2" == 1 ]]; then echo "PASS  $1${3:+  — $3}"; else echo "FAIL  $1${3:+  — $3}"; FAILURES=$((FAILURES + 1)); fi; }
restore() { echo "✗ Stopped; switching $NAME back on so the stack ends as it began." >&2; env -u MAF_PLUGINS make --no-print-directory plugin-on NAME=$NAME >/dev/null 2>&1; rm -rf "$WORK"; exit 130; }
trap restore INT TERM

# The containers of a service, as "id started-at" lines, sorted.
state() { for id in $(docker compose -p "$COMPOSE_PROJECT_NAME" ps -q "$1"); do docker inspect -f '{{.Id}} {{.State.StartedAt}}' "$id"; done | sort; }
ids() { state "$1" | cut -d' ' -f1; }

# Python does the HTTP: one streamed chat run, the availability poll, the 404 probe and the api's view of the domains.
py() { BASE_URL="$BASE_URL" python3 - "$@" <<'PY'
import json, os, sys, time, urllib.error, urllib.request, uuid
base, what = os.environ["BASE_URL"], sys.argv[1]
def call(path, body=None, token=None, timeout=30):
    h = {"content-type": "application/json", **({"authorization": f"Bearer {token}"} if token else {})}
    req = urllib.request.Request(base + path, data=json.dumps(body).encode() if body is not None else None,
                                 method="POST" if body is not None else "GET", headers=h)
    return urllib.request.urlopen(req, timeout=timeout)
def token():
    with call("/dev/token", {"userId": "adam", "tenantId": "firm-a", "role": "USER"}) as r:
        return json.loads(r.read())["token"]
if what == "run":          # one run, its event types on stdout
    run = {"threadId": None, "runId": "r_switch_" + uuid.uuid4().hex[:12],
           "messages": [{"id": "u1", "role": "user", "content": "How is a tool call made idempotent in the code, in detail?"}]}
    types = []
    try:
        with call("/api/chat", run, token(), timeout=300) as r:
            types = [json.loads(l[5:])["type"] for l in (x.decode().strip() for x in r) if l.startswith("data:")]
    except Exception as e:
        types.append(f"ERROR:{type(e).__name__}")
    print(" ".join(types))
elif what == "poll":       # GET /api/plugins every 0.5 s until the stop file appears; failures on stdout
    t, fails, n, why = token(), 0, 0, []
    while not os.path.exists(sys.argv[2]):
        n += 1
        try:
            with call("/api/plugins", token=t, timeout=5) as r: fails += r.status != 200
        except Exception as e:
            fails += 1
            why.append(f"{time.strftime('%H:%M:%S')} {getattr(e, 'code', '') or type(e).__name__}")
        time.sleep(0.5)
    print(f"{fails} {n} {';'.join(why)}")
elif what == "probe":      # status of POST /code/mcp, and whether the web app answered
    try:
        with call("/code/mcp", {}) as r: status, body = r.status, r.read().decode(errors="replace")
    except urllib.error.HTTPError as e: status, body = e.code, e.read().decode(errors="replace")
    print(status, "spa" if 'id="root"' in body else "nospa")
elif what == "domains":    # the codebase domain and the code plugin in 4 answers in a row: "present" / "absent" / "mixed"
    t, seen = token(), set()
    for _ in range(4):
        with call("/api/plugins", token=t) as r: a = json.loads(r.read())
        seen.add(any(d.get("id") == "codebase" for d in a.get("domains", [])) or any(p["name"] == "code" for p in a["plugins"]))
    print("present" if seen == {True} else "absent" if seen == {False} else "mixed")
PY
}

switch() { # $1 = off|on, $2 = expected "absent"|"present", $3 = mcp-code expected "none"|"some"
  local lb_before rt_before api_before
  lb_before="$(state lb)"; rt_before="$(state copilot-runtime)"; api_before="$(state api)"
  rm -f "$WORK/stop"; py poll "$WORK/stop" > "$WORK/poll" &
  local poll=$!
  [[ "$1" == off ]] && { py run > "$WORK/run" & }
  local run=$!
  sleep 2
  step "make plugin-$1 NAME=$NAME"
  # Without the outer make's exported set: each switch reads compose/.env, as a person's own `make plugin-…` does.
  env -u MAF_PLUGINS make --no-print-directory plugin-$1 NAME=$NAME || check "make plugin-$1 succeeds" 0
  touch "$WORK/stop"; wait $poll
  read -r fails polls why < "$WORK/poll"
  check "the api answered through the balancer throughout ($polls polls)" "$([[ "$fails" == 0 ]] && echo 1)" "$fails failed${why:+: $why}"
  if [[ "$1" == off ]]; then
    wait $run; local types; types="$(cat "$WORK/run")"
    check "the run in flight ended finished" "$([[ "${types##* }" == RUN_FINISHED ]] && echo 1)" "${types##* }"
  fi
  local containers; containers="$(docker compose -p "$COMPOSE_PROJECT_NAME" ps -q mcp-code)"
  check "mcp-code: $3" "$([[ ( "$3" == none && -z "$containers" ) || ( "$3" == some && -n "$containers" ) ]] && echo 1)"
  local probe; probe="$(py probe)"
  if [[ "$1" == off ]]; then check "/code/mcp answers 404, not the web app" "$([[ "$probe" == "404 nospa" ]] && echo 1)" "$probe"
  else check "/code/mcp is served again, not 404 and not the web app" "$([[ "$probe" != 404* && "$probe" == *nospa ]] && echo 1)" "$probe"; fi
  local seen; seen="$(py domains)"
  check "4 answers in a row: the codebase domain is $2" "$([[ "$seen" == "$2" ]] && echo 1)" "$seen"
  check "lb untouched (same container, same start)" "$([[ "$(state lb)" == "$lb_before" ]] && echo 1)"
  check "copilot-runtime untouched" "$([[ "$(state copilot-runtime)" == "$rt_before" ]] && echo 1)"
  local api_after; api_after="$(state api)"
  check "each api replica is the same container" "$([[ "$(cut -d' ' -f1 <<<"$api_after")" == "$(cut -d' ' -f1 <<<"$api_before")" ]] && echo 1)"
  check "each api replica was restarted" "$(comm -12 <(echo "$api_before") <(echo "$api_after") | grep -q . && echo 0 || echo 1)"
}

step "the stack is up with $NAME in use"
[[ "$(py domains)" == present ]] || { echo "✗ $NAME is not in use on $BASE_URL: start the dev stack with it first (make)" >&2; exit 2; }
switch off absent none
switch on present some
rm -rf "$WORK"
(( FAILURES == 0 )) && echo "✓ $NAME switched off and on with nothing else restarted" || { echo "✗ $FAILURES check(s) failed"; exit 1; }
