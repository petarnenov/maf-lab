# Design

## Context

See proposal.md — Why. Today `api` has no `user:`, so it runs as root (uid 0). It bind-mounts the repository
read-write at the host's own path (`${MAF_LAB_REPO}:${MAF_LAB_REPO}`, because git worktrees record absolute paths),
plus `../data:/data` and `../evals:/evals` read-write, and keeps SQLite in the named volume `api-data:/app-data`.
What it writes:

- the repository, through `RepoWriter` only: `git worktree add` under `/tmp`, `commit`, `branch -f`, `merge --no-ff` in
  the checkout that has `main` (or in a `/tmp` worktree plus `update-ref`), `branch -D`;
- `evals/*.jsonl` (`DatasetWriter`, feedback rows) and `data/<cache>/contextual-cache.json` (the indexer the admin
  index endpoint runs in-process);
- `/app-data/*.db` (SQLite, WAL), and whatever ASP.NET Core keeps under `HOME` (data-protection keys).

The image already runs `git config --system --add safe.directory '*'`, so git never refuses a repository for "dubious
ownership", whatever the uid. `test-agent` (root, repository `:ro`, own `testagent-work` volume) and
`coverage-runner` (uid 10001, repository `:ro`) write nothing to the host.

## Goals / Non-Goals

**Goals:** nothing the stack does leaves a path in the repository, `data/` or `evals/` owned by anyone but the user
who ran `make`, on Linux and in CI; repair what earlier versions left; macOS unchanged.

**Non-Goals:** running every service as non-root (only the one that writes into the host matters here); rootless
Docker or `userns-remap` hosts (see Risks); changing `RepoWriter`.

## Decisions

### 1. The api runs as the host user, declared in compose

`api` gets `user: "${MAF_LAB_UID:-1000}:${MAF_LAB_GID:-1000}"`. The Makefile sets
`MAF_LAB_UID ?= $(shell id -u)` and `MAF_LAB_GID ?= $(shell id -g)` and exports both, beside `MAF_LAB_REPO` (plain
`?=`/`export`, GNU Make 3.81). Scripts that call `docker compose` themselves run from make recipes and inherit the
exported values; the only compose calls outside make (CI's diagnostics step, a hand-typed `docker compose ps/logs`)
start nothing, and a hand-typed `up` gets 1000:1000, the first user on nearly every Linux machine.

Alternatives considered:
- *Fix ownership after each write* (`chown` in `RepoWriter`, or a post-merge hook). Git writes objects, packs, reflogs,
  `ORIG_HEAD`, the index and working-tree files from several commands; any path missed stays root-owned, and the
  container would still need to know the host uid. Rejected: whack-a-mole.
- *An entrypoint that starts as root, reads the owner of `MAF_LAB_REPO` with `stat` and drops to it (`setpriv`).*
  Needs no Makefile variables, but it puts privilege-dropping logic into the image, infers the user from a `stat` that
  Docker Desktop's file sharing may answer differently, and the container still starts as root. Rejected in favour of
  a declarative `user:` anyone can read in the compose file.
- *A fixed service uid (like the runner's 10001).* That uid would own what it writes on the host — the same problem
  with another number.

### 2. A one-shot `api-data-init` hands the named volume to that user

A new service `api-data-init` (`alpine:3.22`, root, `restart: "no"`) mounts `api-data:/app-data` and runs
`chown -R ${MAF_LAB_UID}:${MAF_LAB_GID} /app-data`; `api` `depends_on` it with `service_completed_successfully`. Both a
fresh volume (Docker creates the mount point as root, since the image has no `/app-data`) and an existing root-owned
one end up owned by the user. The volume holds a few SQLite files, so a recursive chown on every `up` costs nothing.
`alpine:3.22` is already pinned by the CI overlay; using the api image instead would mean a second build of it.
`scripts/wait_healthy.sh` already accepts one-shot services that exit 0.

### 3. `HOME=/tmp` for the api

A uid without a passwd entry gets `HOME=/` from Docker (unwritable); uid 1000 happens to be `ubuntu` in the noble
image. `HOME: /tmp` makes the two cases the same: git finds no global config there (as before), and ASP.NET Core can
write its data-protection keys, which were already ephemeral (container-local `/root`). `/tmp` is where `RepoWriter`
makes its worktrees already (`Path.GetTempPath()`), mode 1777.

Verified: with the api image, uid 1000 (has a passwd entry) and uid 1001 (has none) both ran `worktree add`, `commit`,
`branch -f` without identity variables, `merge` in the checkout, `merge` in a `/tmp` worktree with `update-ref`, and
`branch -D` against a bind-mounted scratch repository, with every resulting path owned by the running uid.

### 4. Repair leftovers from `make up`, detected on the host

`make up` first runs `scripts/repair_ownership.sh`. It runs `find -xdev -user 0` on the host over the checkout and,
when it differs, `MAF_LAB_REPO` (pruning `node_modules`). Only if that finds something does it start
`docker run --rm alpine:3.22` with the root bind-mounted at its own path and `chown -h UID:GID` exactly the listed paths
(NUL-separated through `xargs -0`). It prints one final line: nothing to repair, or how many paths it gave to whom and
how long it took. When `make` runs as root (uid 0) it does nothing.

Why automatic and not a documented one-liner: once the api runs as the user, the first test-generation run after the
upgrade would fail on the root-owned `.git/refs/heads/test-agent/` directory, and the developer's own commits already
fail on root-owned object directories. The only fix without `sudo` is a root container, which is exactly what `make`
can do for them. Least surprise is preserved by the scope: only uid 0, only to the user running `make`, only paths the
host itself reports, never through symlinks, announced with a count.

Why on the host and not inside `api-data-init`: the host's `find` reports true ownership on every platform. Inside a
container on Docker Desktop for macOS, bind-mounted files may report a different owner, and a recursive scan of the
repository over the file-sharing layer is slow; a host `find` is instant (≈10 ms on this repository) and on macOS
finds nothing, so it never starts a container there.

### 5. Out of scope: test-agent and the other services

`test-agent`, `mcp-retrieval` and the rest run as root but write only their own named volumes; `test-agent` and
`coverage-runner` mount the repository read-only. Nothing they do reaches the host's files, so they keep their users.

## Risks / Trade-offs

- [Rootless Docker or `userns-remap`] Container uid N maps to a sub-uid, so the api could not write the user's files and
  its writes would be owned by the sub-uid. → There, container root already *is* the host user: run
  `make MAF_LAB_UID=0 MAF_LAB_GID=0`. Recorded in DECISIONS §70; not auto-detected (it would cost a `docker info` on
  every make invocation).
- [macOS] Not verified on a Mac in this change. Docker Desktop's file sharing grants bind-mount access as the host user
  regardless of the container uid, `id -u`/`id -g` give 501/20 there, the init container chowns the VM-side volume to
  501:20, and the repair step finds nothing on the host. → If a Mac shows a permission error, `MAF_LAB_UID=0` restores
  the old behaviour.
- [CI] The runner user (uid 1001) owns the checkout and the e2e clone; the api runs as 1001 without a passwd entry
  (verified to work for every git operation `RepoWriter` uses). `make ci-e2e`'s `rm -rf` of the clone can no longer
  hit root-owned files.
- [Group] The api runs with the user's primary group only, no supplementary groups. It needs none: every path it
  writes is owned by the user.

## Migration Plan

Merge, then `make` (or `make up`): compose recreates the api containers (new `user:`/`HOME`), runs `api-data-init`
once per `up`, and the repair step fixes any root-owned leftovers before that. Rollback: revert the change; the old
root api can still write everything, including user-owned files.
