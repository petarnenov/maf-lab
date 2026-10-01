# Proposal

## Why

On a Linux host the `api` container runs as root and writes into the repository it bind-mounts read-write: every
test-generation run commits a `test-agent/…` branch and every accept merges into `main`. Git then leaves root-owned
`.git/objects/<xx>` directories, refs, reflogs and working-tree files in the developer's checkout, and the developer's
own `git commit` fails as soon as an object hash lands in a root-owned prefix. Docker Desktop on macOS maps bind
mounts to the host user, so only Linux (and CI) is affected — which is also where it is hardest to notice.

## What Changes

- `make` exports `MAF_LAB_UID` and `MAF_LAB_GID` (from `id -u` / `id -g`, overridable), and the `api` service runs as
  that user. Everything the api writes into the host — the repository, `data/`, `evals/` — is then owned by the person
  who ran `make`.
- A new one-shot service `api-data-init` (root, `alpine:3.22`) hands the `api-data` named volume to that user before
  the api starts; existing root-owned volumes are fixed the same way. The api's `HOME` is `/tmp`, writable by any user.
- `make up` repairs what earlier root runs left behind: before starting the stack it looks on the host for paths owned
  by root under the checkout and the mounted repository and, only if it finds any, gives exactly those paths to the
  user who runs `make` (through a throwaway root container), printing how many it repaired. Nothing owned by anyone
  else is touched; a `make` run as root repairs nothing.
- `test-agent` (repository read-only, its own volume) and `coverage-runner` (already uid 10001, repository read-only)
  are unchanged.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `make-workflow`: the stack writes to the host as the user who runs `make`, and `make up` repairs root-owned leftovers.
- `test-generation-runs`: committing, accepting and discarding a run leave every path they create in the repository
  owned by that user.

## Progress

`make up` gains one step, the ownership check. It is a single host-side `find` that finishes in well under a second;
it prints one final line (nothing to repair, or how many paths were repaired and how long it took), in the same plain
`✓`/`✗` style as the other `make up` steps. It adds no UI action.

## Impact

- Code: `compose/docker-compose.yml` (`api` `user:` and `HOME`, new `api-data-init`), `Makefile` (exports, the repair
  step in `up`), new `scripts/repair_ownership.sh`. No application code, package, route, target, project, model or
  lb location changes.
- Operations: the first `make` after this change recreates the api containers and runs `api-data-init`; on a Linux
  checkout that earlier runs left root-owned paths in, it repairs them once.
- CI: GitHub's runner user (uid 1001) owns the checkout and the e2e clone, so the api runs as 1001 there.

## Documentation impact

- `README.md` — the Coverage section says the api writes to the repository as the user who runs `make`, and that
  `make up` repairs root-owned leftovers from earlier versions.
- `openspec/project.md` — the container list gains `api-data-init` (and `openspec/config.yaml` through `make docs`).
- `DECISIONS.md` — §70 records the decision.
- `CLAUDE.md`, `docs/*.md`, `.github/copilot-instructions.md` — unaffected: none of them describes container users or
  file ownership.
