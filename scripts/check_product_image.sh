#!/usr/bin/env bash
# Checks that the product image variant holds no code of a plugin whose manifest does not allow stage or prod
# (introduce-plugins decision 5e): no such plugin's assembly in the api image, nor a provider plugin's in an MCP server's
# image that was built (introduce-provider-plugins: providers live in every process that asks a model or the decision
# engine), and no such plugin's web part in the web bundle (the loader's module key, or the name each web part declares
# through definePlugin).
# Run after `MAF_IMAGE_VARIANT=product docker compose build api web` (make product-check does both).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJECT="${COMPOSE_PROJECT_NAME:-maf-lab}"
trap 'echo; echo "✗ Stopped; nothing was changed. Run make product-check again." >&2; exit 130' INT TERM

excluded=()
while IFS=$'\t' read -r name envs server; do
  [[ "$envs" == *stage* || "$envs" == *prod* ]] || excluded+=("$name"$'\t'"$server")
done < <(python3 - "$ROOT" <<'PY'
import sys
sys.path.insert(0, sys.argv[1] + "/scripts")
import plugins
for name, p in plugins.discover().items():
    servers = ",".join(c.stem for part in ("server", "lib") for c in (p.folder / part).glob("*.csproj"))
    print(f"{name}\t{','.join(p.environments)}\t{servers}")
PY
)

dlls="$(docker run --rm --entrypoint ls "$PROJECT-api" /app)"
# The MCP servers' images that exist (their services are plugins', built when those plugins are installed).
mcp_dlls=""
for image in mcp-retrieval mcp-code mcp-portfolio; do
  if docker image inspect "$PROJECT-$image" >/dev/null 2>&1; then
    mcp_dlls+="$(docker run --rm --entrypoint ls "$PROJECT-$image" /app | sed "s|^|$image:|")"$'\n'
  fi
done
bundle="$(docker run --rm --entrypoint sh "$PROJECT-web" -c 'cat /usr/share/nginx/html/assets/*.js')"
failures=0
total=${#excluded[@]}
i=0
for entry in ${excluded[@]+"${excluded[@]}"}; do
  i=$((i + 1))
  name="${entry%%$'\t'*}"
  servers="${entry#*$'\t'}"
  printf '[%d/%d] %s\n' "$i" "$total" "$name"
  for server in ${servers//,/ }; do
    if grep -qx "$server.dll" <<<"$dlls"; then echo "✗ the product api image holds $server.dll (plugin $name)"; failures=$((failures + 1)); fi
    if grep -qE ":$server\.dll\$" <<<"$mcp_dlls"; then
      echo "✗ a product MCP server image holds $server.dll (plugin $name)"; failures=$((failures + 1))
    fi
  done
  # The loader's key for the module, or the name definePlugin carries (any quote the minifier chose).
  if grep -qE "plugins/$name/web/index\.ts|name:[\"'\`]$name[\"'\`]" <<<"$bundle"; then
    echo "✗ the product web bundle holds the web part of plugin $name"
    failures=$((failures + 1))
  fi
done
if (( failures > 0 )); then exit 1; fi
echo "✓ the product images hold no dev-or-qa-only plugin code ($total plugin(s) checked)"
