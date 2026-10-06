#!/usr/bin/env bash
# make plugin-new-check (introduce-plugins 6.2): a plugin made by `make plugin-new` builds and keeps the docs in sync.
# It scaffolds one plugin of each kind into plugins/, builds them (the mcp server's project, and the test host that
# compiles the app plugin's server and tests), regenerates the docs and checks them, then removes both and regenerates
# again and checks once more (a plugin's folder deleted leaves the docs in sync): the checkout ends exactly as it began. The mcp kind's start is proven by _example's end-to-end leg.
#
# Progress: one line per step. Stopping: Ctrl+C or SIGTERM removes what it scaffolded, regenerates the docs and exits
# 130; nothing it started outlives it.
set -euo pipefail
cd "$(dirname "$0")/.."
DOTNET="${DOTNET:-dotnet}"
APP=plugin-new-check-app
MCP=plugin-new-check-mcp
before="$(git status --porcelain)"

cleanup() {
  rm -rf "plugins/$APP" "plugins/$MCP"
  python3 scripts/docs.py generate >/dev/null
}
trap 'cleanup; echo "✗ Stopped; what this scaffolded was removed."; exit 130' INT TERM
trap cleanup EXIT

step() { echo "▸ $*"; }

step "scaffold $APP (app) and $MCP (mcp)"
python3 scripts/plugins.py new "$APP" app >/dev/null
python3 scripts/plugins.py new "$MCP" mcp >/dev/null
step "validate their manifests"
python3 scripts/plugins.py validate
step "build the mcp server"
"$DOTNET" build "plugins/$MCP/service/PluginNewCheckMcp.McpServer.csproj" -nologo -v quiet
step "build the test host, which compiles the app plugin's server and tests"
"$DOTNET" build tests/Maf.Lab.Tests/Maf.Lab.Tests.csproj -nologo -v quiet
step "regenerate the docs and check them"
python3 scripts/docs.py generate >/dev/null
python3 scripts/docs.py check

trap - EXIT
cleanup
step "with both removed, the docs are still in sync"
python3 scripts/docs.py check
step "the checkout is as it was"
after="$(git status --porcelain)"
if [ "$before" != "$after" ]; then
  echo "✗ plugin-new-check left the checkout changed:"; diff <(echo "$before") <(echo "$after") || true
  exit 1
fi
echo "✓ a scaffolded plugin of each kind builds and keeps the docs in sync"
