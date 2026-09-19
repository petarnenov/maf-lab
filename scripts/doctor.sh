#!/usr/bin/env bash
# Reports each prerequisite of maf-lab. Never prints secret values. Exit 1 if a required tool is missing.
set -uo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DOTNET="${DOTNET:-dotnet}"
NPM="${NPM:-npm}"
missing=0

ok()   { printf '  \033[32m✓\033[0m %-22s %s\n' "$1" "$2"; }
bad()  { printf '  \033[31m✗\033[0m %-22s %s\n' "$1" "$2"; missing=1; }
warn() { printf '  \033[33m!\033[0m %-22s %s\n' "$1" "$2"; }

echo "maf-lab prerequisites"

if command -v docker >/dev/null 2>&1 && docker info >/dev/null 2>&1; then
  if v=$(docker compose version --short 2>/dev/null); then ok "Docker + compose" "compose $v"; else bad "Docker + compose" "compose plugin missing"; fi
else
  bad "Docker" "not installed or the daemon is not running"
fi

want=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["sdk"]["version"])' "$ROOT/global.json")
if command -v "$DOTNET" >/dev/null 2>&1 || [ -x "$DOTNET" ]; then
  if (cd "$ROOT" && "$DOTNET" --version >/dev/null 2>&1); then
    ok ".NET SDK" "$(cd "$ROOT" && "$DOTNET" --version) (global.json wants $want, rollForward latestPatch)"
  else
    bad ".NET SDK" "found $DOTNET but not SDK $want required by global.json"
  fi
else
  bad ".NET SDK" "not found — install $want (dotnet-install.sh --channel LTS)"
fi

if command -v node >/dev/null 2>&1 && command -v "$NPM" >/dev/null 2>&1; then
  ok "Node / npm" "node $(node -v), npm $("$NPM" -v)"
else
  bad "Node / npm" "not found (Node 24 recommended)"
fi

ok "make" "$(make --version | head -1)"

if [ -n "${OLLAMA_API_KEY:-}" ]; then
  ok "OLLAMA_API_KEY" "set"
else
  warn "OLLAMA_API_KEY" "missing — chat uses Ollama Cloud (gpt-oss:120b) and needs it; export it in your shell"
fi

if curl -sf http://localhost:11434/api/tags >/dev/null 2>&1; then
  ok "host Ollama (optional)" "running on :11434 (models can be shared via OLLAMA_MODELS_DIR)"
else
  warn "host Ollama (optional)" "not running — compose Ollama will pull the embedding models itself"
fi

exit $missing
