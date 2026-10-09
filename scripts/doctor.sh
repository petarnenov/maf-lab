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

if [ -n "${JEV_MAF_LAB:-}" ]; then
  ok jev-key "JEV_MAF_LAB" "set"
else
  warn jev-key "JEV_MAF_LAB" "missing — intent classification uses TypeSafe Jev and needs it (without it nothing is forced to search); export it in your shell"
fi

# The repository optional development tools read and write, at the path compose mounts it.
repo="${MAF_LAB_REPO:-$(pwd)}"
if git -C "$repo" rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  ok repo "MAF_LAB_REPO" "$repo is a git repository"
else
  bad repo "MAF_LAB_REPO" "$repo is not a git repository — development tools need one (make sets it)"
fi

if [ -n "${GITHUB_ISSUES_TOKEN:-}" ]; then
  ok github-token "GITHUB_ISSUES_TOKEN (optional)" "set"
else
  warn github-token "GITHUB_ISSUES_TOKEN (optional)" "missing — suspected bugs the test agent finds are still skipped, but without a GitHub issue; a fine-grained token with Issues read/write on this repository enables them"
fi

# An Ollama of your own on :11434, if you happen to run one — NOT the stack's Ollama, which compose starts
# in a container on :11435. This only decides whether compose can reuse models you already pulled.
if curl -sf http://localhost:11434/api/tags >/dev/null 2>&1; then
  ok host-ollama "host Ollama (optional)" "running on :11434 — the stack can reuse its models (OLLAMA_MODELS_DIR)"
else
  warn host-ollama "host Ollama (optional)" "not running, and not needed — this is a separate host Ollama, not the stack's own on :11435"
fi

# The two compose Ollamas are pinned to CPU sets of Docker's VM (queries on OLLAMA_INTERACTIVE_CPUS, documents on
# OLLAMA_BATCH_CPUS), and each is told its thread count. A set past the VM's CPUs fails the container's start; a thread
# count other than the set's size oversubscribes or idles CPUs.
cpu_count() { # "0-3" → 4, "0,2,4-5" → 4
  echo "$1" | tr ',' '\n' | awk -F- '{ n += ($2 == "" ? 1 : $2 - $1 + 1) } END { print n }'
}
cpu_max() { # highest CPU index in a set
  echo "$1" | tr ',-' '\n\n' | sort -n | tail -1
}
if vm_cpus=$(docker info --format '{{.NCPU}}' 2>/dev/null) && [ -n "$vm_cpus" ]; then
  i_cpus="${OLLAMA_INTERACTIVE_CPUS:-0-3}" b_cpus="${OLLAMA_BATCH_CPUS:-4-15}"
  i_threads="${OLLAMA_INTERACTIVE_THREADS:-4}" b_threads="${OLLAMA_BATCH_THREADS:-12}"
  highest=$(cpu_max "$i_cpus"); [ "$(cpu_max "$b_cpus")" -gt "$highest" ] && highest=$(cpu_max "$b_cpus")
  if [ "$highest" -ge "$vm_cpus" ]; then
    warn ollama-cpus "Ollama CPU sets" "interactive $i_cpus, batch $b_cpus — Docker has only $vm_cpus CPUs (0-$((vm_cpus - 1))); set OLLAMA_INTERACTIVE_CPUS/OLLAMA_BATCH_CPUS and the matching *_THREADS"
  elif [ "$(cpu_count "$i_cpus")" != "$i_threads" ] || [ "$(cpu_count "$b_cpus")" != "$b_threads" ]; then
    warn ollama-cpus "Ollama CPU sets" "thread counts ($i_threads, $b_threads) do not match the sets' sizes ($(cpu_count "$i_cpus"), $(cpu_count "$b_cpus")); set OLLAMA_INTERACTIVE_THREADS/OLLAMA_BATCH_THREADS"
  else
    ok ollama-cpus "Ollama CPU sets" "interactive $i_cpus ($i_threads threads), batch $b_cpus ($b_threads threads) of $vm_cpus"
  fi
fi

# The graph store, when the stack (or make infra) is up: the host-side indexer reaches it on loopback.
if (exec 3<>/dev/tcp/127.0.0.1/7687) 2>/dev/null; then
  ok neo4j "Neo4j (when running)" "listening on 127.0.0.1:7687 — make graph can build the graph"
else
  warn neo4j "Neo4j (when running)" "not listening on 127.0.0.1:7687 — 'make' or 'make infra' starts it"
fi

exit $missing
