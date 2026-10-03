#!/bin/sh
# Pulls the models listed in $MODELS into the compose Ollama. Models already present are skipped (no network needed),
# and pulls are retried so a transient registry/DNS failure does not block the stack.
set -e
for model in $MODELS; do
  if ollama show "$model" >/dev/null 2>&1; then
    echo "present: $model"
    continue
  fi
  attempt=1
  until ollama pull "$model"; do
    if [ "$attempt" -ge 5 ]; then
      echo "failed to pull $model after $attempt attempts" >&2
      exit 1
    fi
    attempt=$((attempt + 1))
    echo "retrying $model (attempt $attempt)"
    sleep $((attempt * 3))
  done
done
# Load each model now, so the first search after a start does not pay the cold load (the server keeps it loaded).
for model in $MODELS; do
  ollama run "$model" "warm up" >/dev/null || echo "could not warm $model" >&2
done
echo "models ready"
