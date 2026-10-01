# Tasks

## 1. The api runs as the host user

- [x] 1.1 Export `MAF_LAB_UID`/`MAF_LAB_GID` from the Makefile (`?=`, `id -u`/`id -g`); verify `make -n up` and `make -n up CI_MODE=1` show them in the recipe environment
- [x] 1.2 Give `api` `user: "${MAF_LAB_UID:-1000}:${MAF_LAB_GID:-1000}"` and `HOME: /tmp`; verify `docker compose -f compose/docker-compose.yml config -q` passes with `MAF_LAB_REPO` set and `config` shows the user
- [x] 1.3 Add the one-shot `api-data-init` (`alpine:3.22`, `chown -R` of `api-data`) and make `api` depend on it completing; verify a root-owned scratch volume is owned by the uid afterwards
- [x] 1.4 Verify the api image runs as the host uid against a scratch data volume (after the init), answers `/health` with 200 and writes its SQLite files as that uid
- [x] 1.5 Verify, with the api image as the host uid and as a passwd-less uid (1001), that every git operation `RepoWriter` uses leaves only that uid's paths in a bind-mounted scratch repository

## 2. Repair leftovers

- [x] 2.1 Add `scripts/repair_ownership.sh` (host `find -user 0`, chown only the hits through a throwaway root container, skip as root, one final line) and run it first in `make up`; verify against a scratch directory with root-owned, user-owned and other-uid paths and a symlink: only the root-owned paths change, and a clean tree starts no container

## 3. Documentation

- [x] 3.1 README Coverage section: the api writes as the user who runs `make`, and `make up` repairs root-owned leftovers
- [x] 3.2 `openspec/project.md` container list gains `api-data-init`; run `make docs` (never by hand inside a `generated:` block) and `make docs-check`
- [x] 3.3 DECISIONS §70 for this change

## 4. Integration

- [x] 4.1 `make lint-dotnet`, `openspec validate --specs --strict` and `openspec validate run-the-api-as-the-host-user --strict` pass
