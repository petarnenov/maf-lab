#!/usr/bin/env bash
# make ci-e2e: the model-free end-to-end run in its own compose project, so nothing it writes (coverage snapshots at
# the clone's commits, chat history, the index) reaches the dev stack's volumes. The ports are the same, so the dev
# stack is stopped first — with its volumes kept. A pass removes the e2e project and its volumes; a failure leaves it
# running for inspection (CI collects its logs).
# Usage: scripts/ci_e2e.sh <e2e-repo> [extra make arguments]
set -uo pipefail
E2E_REPO="$1"
shift
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DEV_PROJECT="${DEV_PROJECT:-maf-lab}"
E2E_PROJECT="${E2E_PROJECT:-maf-lab-e2e}"
FILES=(-f "$ROOT/compose/docker-compose.yml" -f "$ROOT/compose/docker-compose.ci.yml")

echo "▸ Stopping the dev stack ($DEV_PROJECT, volumes kept) — the e2e stack needs its ports"
docker compose -p "$DEV_PROJECT" -f "$ROOT/compose/docker-compose.yml" down --remove-orphans

if make -C "$ROOT" up index-if-empty verify eval-a2a testgen-e2e \
    CI_MODE=1 MAF_LAB_REPO="$E2E_REPO" COMPOSE_PROJECT="$E2E_PROJECT" "$@"; then
  echo "▸ Removing the e2e stack ($E2E_PROJECT) and its volumes"
  MAF_LAB_REPO="$E2E_REPO" docker compose -p "$E2E_PROJECT" "${FILES[@]}" down -v --remove-orphans
  echo "✓ e2e passed. Run 'make' to bring the dev stack back."
else
  echo "✗ e2e failed. The stack is left running as '$E2E_PROJECT' for inspection; remove it with:"
  echo "  MAF_LAB_REPO=$E2E_REPO docker compose -p $E2E_PROJECT ${FILES[*]} down -v --remove-orphans"
  exit 1
fi
