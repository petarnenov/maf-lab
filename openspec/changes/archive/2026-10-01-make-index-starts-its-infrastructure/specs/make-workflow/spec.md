## ADDED Requirements

### Requirement: Index targets start their infrastructure
`make infra` SHALL start only the services the host-side indexer needs — Qdrant, the compose Ollama, and the
one-shot model pull that makes the embedding model present — and SHALL wait until they are healthy, exiting non-zero
with the failing service named if they do not become healthy. It SHALL NOT start, stop, rebuild or scale any
application service. `make index`, `index-portfolio`, `index-code`, `reindex`, `drift`, `rebuild-index` and
`migrate` SHALL run `make infra` first, so each works from a stopped stack. The target SHALL behave the same on
macOS (GNU Make 3.81, bash 3.2, Docker Desktop) and Linux (Docker Engine with the compose plugin), and SHALL show
progress while it waits, per `progress-feedback`.

#### Scenario: Stack stopped
- **WHEN** the stack is down (`make down`, or a fresh boot) and `make index` runs
- **THEN** Qdrant and Ollama are started and become healthy, the embedding model is pulled if missing, and every
  corpus is indexed; the application services stay stopped

#### Scenario: Stack already up
- **WHEN** the full stack is running and `make index` runs
- **THEN** `make infra` changes no container and finishes in about a second, and indexing proceeds as before

#### Scenario: Infrastructure cannot start
- **WHEN** Docker is not running, or Qdrant does not become healthy
- **THEN** the target exits non-zero before the indexer runs, naming Docker or the unhealthy service

#### Scenario: macOS
- **WHEN** a developer on macOS with Docker Desktop and the system make (3.81) runs `make index` from a stopped stack
- **THEN** it behaves as on Linux
