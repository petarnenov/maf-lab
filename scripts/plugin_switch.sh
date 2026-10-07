#!/usr/bin/env bash
# Installs or removes one plugin on the running stack (introduce-plugins decision 2).
#   on  NAME: the plugin's services → healthy → its lb snippet → reload → plugins/.installed → publish
#   off NAME: its open work (refused unless STOP_WORK=1, which stops it through its store) → .installed → snippet
#             removed → reload → its services removed
# A plugin with an in-process server part also restarts the api replicas one at a time (scripts/api_restart.sh).
# api, lb and copilot-runtime are never recreated: a plugin's compose file holds only its own services.
# Ctrl+C stops between steps, never inside one, and exits 130. compose/.env, plugins/.installed and conf.d are written
# by rename, so each holds the old or the new set, never half of one.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
# compose mounts the repository at MAF_LAB_REPO (make exports it); outside make, it is this checkout.
export MAF_LAB_REPO="${MAF_LAB_REPO:-$ROOT}"
ACTION="${1:-}"
NAME="${2:-}"
BASE_URL="${BASE_URL:-http://localhost:7171}"
PLUGINS=(python3 "$ROOT/scripts/plugins.py")
COMPOSE=(docker compose -p "${COMPOSE_PROJECT_NAME:-maf-lab}")
ENV_FILE="$ROOT/compose/.env"
# As make does: the environment wins, then compose/.env (so the script alone sees the same set make would).
for var in MAF_PLUGINS MAF_ENV; do
  if [[ -z "${!var+x}" && -f "$ENV_FILE" ]] && line="$(grep -E "^$var=" "$ENV_FILE" | tail -1)"; then
    export "$var=${line#*=}"
  fi
done

[[ "$ACTION" == on || "$ACTION" == off ]] || { echo "usage: plugin_switch.sh on|off NAME" >&2; exit 2; }
[[ -n "$NAME" ]] || { echo "✗ say which plugin: make plugin-$ACTION NAME=<plugin> (make plugins lists them)" >&2; exit 2; }
# A provider lives in every process that asks a model, an embedder or the decision engine, and no rolling restart covers
# them all (introduce-provider-plugins): it changes only with the whole stack.
if [[ "$("${PLUGINS[@]}" kind "$NAME" 2>/dev/null)" == provider ]]; then
  echo "✗ $NAME is a provider: set MAF_CORE_PROVIDERS (or MAF_PLUGINS) and run make up" >&2
  exit 2
fi

# ── progress and stopping ────────────────────────────────────────────────────────────────────────────────────────
STOP=0
trap 'STOP=1; echo; echo "… stopping after the current step" >&2' INT TERM
STEP=0
TOTAL=0
step() {
  STEP=$((STEP + 1))
  if [[ -t 1 ]]; then
    local width=24 done=$((STEP * 24 / (TOTAL > 0 ? TOTAL : 1)))
    printf '\r[%-*s] %d/%d %s\033[K' "$width" "$(printf '%*s' "$done" '' | tr ' ' '#')" "$STEP" "$TOTAL" "$1"
  else
    printf '[%d/%d] %s\n' "$STEP" "$TOTAL" "$1"
  fi
}
# Runs one step's command shielded from Ctrl+C, so a stop lands between steps, never inside one. The step runs in the
# background and is waited for: a trapped signal interrupts `wait` (so the trap runs at once and STOP is set), and the
# step is then waited for again until it has finished. Bash would otherwise drop a SIGINT its foreground child survived.
shielded() {
  ( trap '' INT TERM; "$@" ) &
  local pid=$! status=0
  while :; do
    wait "$pid" && status=0 || status=$?
    kill -0 "$pid" 2>/dev/null || break
  done
  return "$status"
}
checkpoint() {
  if [[ "$STOP" == 1 ]]; then
    [[ -t 1 ]] && echo
    local now
    now="$(installed_list | paste -sd, -)"
    echo "Stopped — plugins installed: ${now:-none}; run 'make plugin-$ACTION NAME=$NAME' again to finish" >&2
    exit 130
  fi
}
finish() { [[ -t 1 ]] && echo; echo "✓ $1"; }
# A step's own output stays out of the progress bar, but a step that fails shows it: nothing is swallowed.
STEP_LOG="$(mktemp -t plugin-switch.XXXXXX)"
trap 'rm -f "$STEP_LOG"' EXIT
quiet() {
  local status=0
  "$@" >"$STEP_LOG" 2>&1 || status=$?
  if [[ "$status" != 0 ]]; then
    [[ -t 1 ]] && echo
    echo "✗ the step failed (exit $status); its output:" >&2
    cat "$STEP_LOG" >&2
  fi
  return "$status"
}

installed_list() { "${PLUGINS[@]}" resolve 2>/dev/null || true; }
compose_files_for() { MAF_PLUGINS="$1" "${PLUGINS[@]}" compose-files; }
compose_file_env() {
  # The core's files (as make exports them) plus the plugin files of the set given.
  local core="${COMPOSE_FILE:-$ROOT/compose/docker-compose.yml}" extra
  core="$(tr ':' '\n' <<<"$core" | grep -v "^$ROOT/plugins/" | paste -sd: -)"
  extra="$(compose_files_for "$1" | paste -sd: -)"
  echo "${core}${extra:+:$extra}"
}
set_env_line() {
  # compose/.env keeps every other line; MAF_PLUGINS is replaced, written by rename.
  local tmp="$ENV_FILE.tmp-$$"
  { [[ -f "$ENV_FILE" ]] && grep -v '^MAF_PLUGINS=' "$ENV_FILE" || true; echo "MAF_PLUGINS=$1"; } >"$tmp"
  mv "$tmp" "$ENV_FILE"
}
reload_lb() {
  "${COMPOSE[@]}" exec -T lb sh -c 'nginx -t -c /etc/nginx/lb/nginx.conf -q && nginx -c /etc/nginx/lb/nginx.conf -s reload 2>&1 | grep -v "\[notice\]" || true'
}
publish() { "${COMPOSE[@]}" exec -T redis redis-cli PUBLISH plugins-changed "$ACTION:$NAME" >/dev/null 2>&1 || true; }
admin_token() {
  curl -fsS -X POST "$BASE_URL/dev/token" -H 'Content-Type: application/json' \
    -d '{"userId":"alice","tenantId":"firm-a","role":"TENANT_ADMIN"}' | python3 -c 'import json,sys; print(json.load(sys.stdin)["token"])'
}

current="$(installed_list | paste -sd, -)"

if [[ "$ACTION" == on ]]; then
  if [[ ",$current," == *",$NAME,"* ]]; then echo "✓ $NAME is already installed"; exit 0; fi
  next="${current:+$current,}$NAME"
  # Resolve first: a missing plugin, a cycle or an environment the plugin does not allow stops here, before anything.
  new_set="$(MAF_PLUGINS="$next" "${PLUGINS[@]}" resolve | paste -sd, -)" || exit 2
  added=()
  for p in ${new_set//,/ }; do [[ ",$current," == *",$p,"* ]] || added+=("$p"); done
  restart=0
  for p in ${added[@]+"${added[@]}"}; do if "${PLUGINS[@]}" has-server "$p"; then restart=1; fi; done
  services=()
  for p in ${added[@]+"${added[@]}"}; do while read -r s; do [[ -n "$s" ]] && services+=("$s"); done < <("${PLUGINS[@]}" services "$p"); done
  TOTAL=$(( (${#services[@]} > 0 ? 2 : 0) + 3 + restart ))
  if [[ "$restart" == 1 ]]; then
    echo "ℹ ${added[*]-} runs inside the api: its replicas will be restarted one at a time, each draining its runs first"
  fi
  if (( ${#services[@]} > 0 )); then
    step "starting ${services[*]-}"
    COMPOSE_FILE="$(compose_file_env "$new_set")" quiet shielded "${COMPOSE[@]}" up -d --build --no-deps ${services[@]+"${services[@]}"}
    checkpoint
    step "waiting until healthy"
    COMPOSE_FILE="$(compose_file_env "$new_set")" quiet shielded "$ROOT/scripts/wait_healthy.sh" "${WAIT_TIMEOUT:-300}"
    checkpoint
  fi
  step "adding its routes to the balancer"
  MAF_PLUGINS="$new_set" shielded "${PLUGINS[@]}" install --conf-d-only
  shielded reload_lb
  checkpoint
  step "recording the installed set"
  set_env_line "$new_set"
  MAF_PLUGINS="$new_set" shielded "${PLUGINS[@]}" install --installed-only
  checkpoint
  if [[ "$restart" == 1 ]]; then
    step "restarting the api replicas"
    # The new set, not the environment's: make exported the set it started with, and api_restart.sh regenerates the
    # balancer's parts from MAF_PLUGINS after each replica.
    MAF_PLUGINS="$new_set" shielded "$ROOT/scripts/api_restart.sh"
    checkpoint
  fi
  step "telling the running services"
  publish
  finish "$NAME installed (installed: $new_set)"
else
  if [[ ",$current," != *",$NAME,"* ]]; then echo "✓ $NAME is not installed"; exit 0; fi
  next="$(tr ',' '\n' <<<"$current" | { grep -vx "$NAME" || true; } | paste -sd, -)"
  # A plugin another installed plugin depends on cannot leave alone.
  if ! new_set="$(MAF_PLUGINS="${next:-none}" "${PLUGINS[@]}" resolve 2>&1 | paste -sd, -)" || [[ ",$new_set," == *",$NAME,"* ]]; then
    echo "✗ cannot remove $NAME: another installed plugin depends on it ($new_set)" >&2
    exit 2
  fi
  restart=0
  if "${PLUGINS[@]}" has-server "$NAME"; then restart=1; fi
  services=()
  while read -r s; do [[ -n "$s" ]] && services+=("$s"); done < <("${PLUGINS[@]}" services "$NAME")
  TOTAL=$(( 3 + (${#services[@]} > 0 ? 1 : 0) + restart + 1 ))
  step "checking its open work"
  open="[]"
  if [[ "$restart" == 1 ]]; then
    token="$(admin_token)"
    open="$(curl -fsS "$BASE_URL/api/plugins/$NAME/open-work" -H "Authorization: Bearer $token")"
  fi
  count="$(python3 -c 'import json,sys; print(len(json.loads(sys.argv[1])))' "$open")"
  if [[ "$count" != 0 ]]; then
    if [[ "${STOP_WORK:-0}" != 1 ]]; then
      [[ -t 1 ]] && echo
      echo "✗ $NAME has $count item(s) of open work; run 'make plugin-off NAME=$NAME STOP_WORK=1' to stop them first:" >&2
      python3 -c 'import json,sys; [print(f"  - {w[\"kind\"]} {w[\"id\"]} ({w[\"state\"]})") for w in json.loads(sys.argv[1])]' "$open" >&2
      exit 1
    fi
    curl -fsS -X POST "$BASE_URL/api/plugins/$NAME/open-work/cancel" -H "Authorization: Bearer $token" >/dev/null
    # Waiting for the stopped work's own state to say so: an indeterminate wait. Ctrl+C leaves the cancels in place.
    trap 'echo; echo "Stopped while waiting; the cancels already issued stay in place (they live in the work stores)" >&2; exit 130' INT TERM
    start=$SECONDS
    while :; do
      left="$(curl -fsS "$BASE_URL/api/plugins/$NAME/open-work" -H "Authorization: Bearer $token" | python3 -c 'import json,sys; print(len(json.load(sys.stdin)))')"
      [[ "$left" == 0 ]] && break
      printf '\r… waiting for %s item(s) to stop (%ds)\033[K' "$left" $((SECONDS - start))
      sleep 1
    done
    trap 'STOP=1; echo; echo "… stopping after the current step" >&2' INT TERM
  fi
  checkpoint
  step "recording the installed set"
  set_env_line "${next:-none}"
  MAF_PLUGINS="${next:-none}" shielded "${PLUGINS[@]}" install --installed-only
  publish
  checkpoint
  step "removing its routes from the balancer"
  MAF_PLUGINS="${next:-none}" shielded "${PLUGINS[@]}" install --conf-d-only
  shielded reload_lb
  checkpoint
  if (( ${#services[@]} > 0 )); then
    step "stopping ${services[*]-}"
    COMPOSE_FILE="$(compose_file_env "$current")" quiet shielded "${COMPOSE[@]}" rm -sf ${services[@]+"${services[@]}"}
    checkpoint
  fi
  if [[ "$restart" == 1 ]]; then
    step "restarting the api replicas"
    # The new set, not the environment's (see plugin-on): the removed plugin's parts must not come back.
    MAF_PLUGINS="${next:-none}" shielded "$ROOT/scripts/api_restart.sh"
    checkpoint
  fi
  step "done"
  finish "$NAME removed (installed: ${next:-none})"
fi
