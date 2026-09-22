#!/usr/bin/env bash
# Reports each prerequisite of maf-lab. Never prints secret values. Exit 1 if a required tool is missing.
# --porcelain prints "status<TAB>key<TAB>detail" instead of the table. scripts/setup.sh reads that, so what
# counts as missing is decided here and nowhere else.
set -uo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DOTNET="${DOTNET:-dotnet}"
NPM="${NPM:-npm}"
PORCELAIN=0
[ "${1:-}" = "--porcelain" ] && PORCELAIN=1
missing=0

# say <status> <key> <label> <detail> — the key is the stable name setup.sh dispatches on; the label is cosmetic.
say() {
  if [ "$PORCELAIN" = 1 ]; then printf '%s\t%s\t%s\n' "$1" "$2" "$4"; return; fi
  case "$1" in
    ok)   printf '  \033[32m✓\033[0m %-22s %s\n' "$3" "$4" ;;
    bad)  printf '  \033[31m✗\033[0m %-22s %s\n' "$3" "$4" ;;
    warn) printf '  \033[33m!\033[0m %-22s %s\n' "$3" "$4" ;;
  esac
}
ok()   { say ok   "$@"; }
bad()  { say bad  "$@"; missing=1; }
warn() { say warn "$@"; }

[ "$PORCELAIN" = 1 ] || echo "maf-lab prerequisites"

if command -v docker >/dev/null 2>&1 && docker info >/dev/null 2>&1; then
  if v=$(docker compose version --short 2>/dev/null); then ok docker "Docker + compose" "compose $v"; else bad docker "Docker + compose" "compose plugin missing"; fi
else
  bad docker "Docker" "not installed or the daemon is not running"
fi

want=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["sdk"]["version"])' "$ROOT/global.json")
if command -v "$DOTNET" >/dev/null 2>&1 || [ -x "$DOTNET" ]; then
  if (cd "$ROOT" && "$DOTNET" --version >/dev/null 2>&1); then
    ok dotnet ".NET SDK" "$(cd "$ROOT" && "$DOTNET" --version) (global.json wants $want, rollForward latestPatch)"
  else
    bad dotnet ".NET SDK" "found $DOTNET but not SDK $want required by global.json"
  fi
else
  bad dotnet ".NET SDK" "not found — install $want (dotnet-install.sh --channel LTS)"
fi

if command -v node >/dev/null 2>&1 && command -v "$NPM" >/dev/null 2>&1; then
  ok node "Node / npm" "node $(node -v), npm $("$NPM" -v)"
else
  bad node "Node / npm" "not found (Node 24 recommended)"
fi

ok make "make" "$(make --version | head -1)"

if [ -n "${OLLAMA_API_KEY:-}" ]; then
  ok ollama-key "OLLAMA_API_KEY" "set"
else
  warn ollama-key "OLLAMA_API_KEY" "missing — chat uses Ollama Cloud (gpt-oss:120b) and needs it; export it in your shell"
fi

# An Ollama of your own on :11434, if you happen to run one — NOT the stack's Ollama, which compose starts
# in a container on :11435. This only decides whether compose can reuse models you already pulled.
if curl -sf http://localhost:11434/api/tags >/dev/null 2>&1; then
  ok host-ollama "host Ollama (optional)" "running on :11434 — the stack can reuse its models (OLLAMA_MODELS_DIR)"
else
  warn host-ollama "host Ollama (optional)" "not running, and not needed — this is a separate host Ollama, not the stack's own on :11435"
fi

exit $missing
