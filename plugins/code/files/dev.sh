#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../service"
exec "$DOTNET" run --no-build
