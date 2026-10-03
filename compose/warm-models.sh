#!/bin/sh
# Loads each model in $MODELS into each Ollama instance in $INSTANCES ("url=threads ..."), with that instance's thread
# count. The thread count must be the one the services send: a request with another value (or none) reloads the runner.
set -e
for instance in $INSTANCES; do
  url=${instance%=*}
  threads=${instance##*=}
  for model in $MODELS; do
    wget -q -O /dev/null -T 300 --header 'Content-Type: application/json' \
      --post-data "{\"model\":\"$model\",\"input\":[\"warm up\"],\"options\":{\"num_thread\":$threads}}" \
      "$url/api/embed" || { echo "could not warm $model on $url" >&2; exit 1; }
    echo "warm: $model on $url ($threads threads)"
  done
done
