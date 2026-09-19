#!/bin/sh
# Pulls the models listed in $MODELS into the compose Ollama. Idempotent: already-present models are skipped quickly.
set -e
for model in $MODELS; do
  echo "pulling $model"
  ollama pull "$model"
done
echo "models ready"
