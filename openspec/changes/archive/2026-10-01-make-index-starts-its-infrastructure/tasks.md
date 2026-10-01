# Tasks

No Jev call is added or changed, so the Jev review checklist does not apply.

## 1. Make starts the indexer's infrastructure

- [x] 1.1 Add the `infra` target (`$(COMPOSE) up -d --wait qdrant ollama`, then `$(COMPOSE) up --no-log-prefix ollama-init`) with a `##` description, add it to `.PHONY`, and verify `make infra` from a stopped stack leaves exactly qdrant and ollama running and healthy
- [x] 1.2 Make `index`, `index-portfolio`, `index-code`, `reindex`, `drift`, `rebuild-index` and `migrate` depend on `infra`; verify `make down && make index` indexes all three corpora and the app services stay stopped
- [x] 1.3 Verify the repeat path: with the stack up, `make index` changes no container and each corpus ends `done N/N` in about a second
- [x] 1.4 Verify portability: the target uses only Make 3.81 syntax and compose v2 flags available on Docker Desktop for macOS (no GNU-only shell, `sed` or `make` features); check with `make -n index` and by reading the recipe

## 2. The indexer names an unreachable service

- [x] 2.1 Add a static helper in `src/Maf.Lab.Indexing` that maps an exception to an "unreachable" line — gRPC `Unavailable` → Qdrant `Host:GrpcPort`, `HttpRequestException` over a `SocketException` → the embedding endpoint's authority, anything else → none — never including the exception message
- [x] 2.2 Catch in `Program.Main`: on a mapped exception print the line to stderr and return 1; verify with Qdrant stopped that `make index` prints the line, no stack trace, no core dump, exit 1
- [x] 2.3 Unit tests for the helper in `tests/` (Qdrant, embedding endpoint, unrelated exception → null, message text absent); verify `make test-dotnet` passes the new tests and `make lint-dotnet` is clean

## 3. Documentation

- [x] 3.1 Run `make docs` so the generated make-target lists gain `infra` (README, and any other generated block that lists targets); confirm no hand-written text claims `make index` needs a running stack
- [x] 3.2 Run `make docs-check` and verify it passes
