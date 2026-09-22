#!/usr/bin/env bash
# Installs what 'make doctor' reports missing. What counts as missing is doctor.sh's call, never this script's.
#
# Only unattended, per-user installs run here: the .NET SDK version global.json pins, into ~/.dotnet.
# Anything needing admin rights (Docker), a version-manager choice (Node) or a secret (OLLAMA_API_KEY)
# is printed as the exact command to run, and never executed on your behalf.
set -uo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DOTNET="${DOTNET:-dotnet}"
NPM="${NPM:-npm}"
DOTNET_INSTALL_DIR="${DOTNET_INSTALL_DIR:-$HOME/.dotnet}"

step() { printf '\n\033[36m▸ %s\033[0m\n' "$1"; }
note() { printf '  %s\n' "$1"; }
manual=0
todo() { manual=1; printf '\n\033[33m▸ %s — install it yourself\033[0m\n' "$1"; }

have() { command -v "$1" >/dev/null 2>&1; }

install_dotnet() {
  local want tmp rc
  want=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["sdk"]["version"])' "$ROOT/global.json")
  step ".NET SDK $want → $DOTNET_INSTALL_DIR"
  note "global.json pins this exact version, so 'latest' from a package manager is not a substitute."
  tmp=$(mktemp -d) || return 1
  trap 'rm -rf "$tmp"' RETURN
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$tmp/dotnet-install.sh" || { note "could not download dot.net/v1/dotnet-install.sh"; return 1; }
  bash "$tmp/dotnet-install.sh" --version "$want" --install-dir "$DOTNET_INSTALL_DIR" --no-path
  rc=$?
  [ $rc -eq 0 ] || return $rc
  note "make finds it at \$HOME/.dotnet/dotnet on its own. For 'dotnet' in your own shell, add to ~/.zshrc:"
  note "    export DOTNET_ROOT=\"\$HOME/.dotnet\"; export PATH=\"\$DOTNET_ROOT:\$PATH\""
}

instruct_docker() {
  todo "Docker Desktop"
  note "Needs admin rights and a GUI app to start the daemon, so it is not installed unattended."
  if have brew; then note "    brew install --cask docker-desktop && open -a Docker"
  else note "    https://www.docker.com/products/docker-desktop/  (then start Docker.app)"; fi
  note "The compose plugin ships with Docker Desktop; 'make doctor' also fails when the daemon is simply not running."
}

instruct_node() {
  todo "Node.js 24 + npm"
  note "Which version manager owns Node is your call — DECISIONS.md pins the major (Node 24), not the installer."
  if have brew; then note "    brew install node@24"; fi
  note "    nvm install 24     # or: https://nodejs.org/en/download"
}

instruct_make() {
  todo "make"
  note "You are somehow running make without make. Install Xcode command line tools: xcode-select --install"
}

failed=0
missing=$("$ROOT/scripts/doctor.sh" --porcelain | awk -F'\t' '$1 == "bad" { print $2 }')

if [ -z "$missing" ]; then
  echo "Nothing to install — every required prerequisite is already in place."
else
  for key in $missing; do
    case "$key" in
      dotnet) install_dotnet || { failed=1; printf '\033[31m  ✗ the .NET SDK install failed\033[0m\n'; } ;;
      docker) instruct_docker ;;
      node)   instruct_node ;;
      make)   instruct_make ;;
      *)      todo "$key"; note "No installer for this one — see 'make doctor'." ;;
    esac
  done
fi

# OLLAMA_API_KEY is a secret: reported, never written to a file and never echoed back.
if [ -z "${OLLAMA_API_KEY:-}" ]; then
  manual=1
  printf '\n\033[33m▸ OLLAMA_API_KEY — export it yourself\033[0m\n'
  note "Chat runs gpt-oss:120b on Ollama Cloud. A secret is never written to a file by this script:"
  note "    export OLLAMA_API_KEY=...   # in your shell profile"
fi

printf '\n'
DOTNET="$DOTNET" NPM="$NPM" "$ROOT/scripts/doctor.sh"
rc=$?
if [ $rc -ne 0 ] || [ $failed -ne 0 ]; then
  printf '\n\033[31mStill incomplete.\033[0m Run the commands above, then "make setup" again.\n'
  exit 1
fi
[ $manual -eq 0 ] || printf '\nRequired prerequisites are in place; the items above are optional or yours to set.\n'
