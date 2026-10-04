# Design

## Context

Make starts compose with `-f compose/docker-compose.yml`, so compose's project directory is `compose/` and it reads
`compose/.env` on its own. Make's own variables, and so `HOST_ENV` and `scripts/doctor.sh`, come from the
environment only. `HOST_ENV` expands `$${OLLAMA_*_THREADS:-…}` in the recipe's shell, and the doctor reads
`OLLAMA_*_CPUS` the same way. The Makefile must stay compatible with GNU Make 3.81 (the macOS default).

## Goals / Non-Goals

**Goals:**
- One machine-local file holds the per-host settings. Compose and make agree on them.

**Non-Goals:**
- Full dotenv syntax: no quotes, no `export`, no interpolation, no multi-line values.
- Changing the CPU defaults, or deriving them from the VM's CPU count.
- A second env file, or reading `compose/.env` from scripts that make does not start.

## Decisions

- **`?=` assignment, then `export`.** Each line becomes `KEY?=value`. Make's `?=` skips a variable that is already
  set, and environment and command-line variables count as set. So both win over the file, as they do with compose.
  - *Rejected: a plain `include compose/.env`.* A file assignment would override the environment, the opposite of
    compose.
  - *Rejected: `make -e`.* It would also let the environment override every Makefile default.
- **One `sed` pass and `$(eval)`, with a space placeholder.** `$(shell)` joins its output on spaces. Each space inside
  a line becomes `__SP__` before the split and is restored before `$(eval)`, so values with spaces survive. All of
  this works in Make 3.81.
  - *Rejected: generating a make fragment into a file and including it.* That leaves a build artefact and adds a
    rebuild rule for nothing.
- **Secrets filtered by name.** Lines for `JEV_MAF_LAB`, or a name ending in `_KEY`, `_TOKEN`, `_SECRET` or
  `_PASSWORD`, are dropped before assignment and export. This keeps make-workflow's rule that secrets come only from
  the environment.
  - Compose still reads the whole file for its own interpolation; this change does not alter that.
  - *Rejected: an allowlist of `OLLAMA_*`.* The file should work for any non-secret setting compose already accepts.

## Risks / Trade-offs

- **A quoted value means one thing to compose and another to make** → documented: values are literal. The current
  file holds only CPU sets and counts.
- **A secret with a name the filter misses would be read from the file** → the names are the project's known
  secrets (`OLLAMA_API_KEY`, `JEV_MAF_LAB`, `GITHUB_ISSUES_TOKEN`, `AUTH_SIGNING_KEY`, `*_CLIENT_SECRET`,
  `NEO4J_PASSWORD`). The README says secrets do not belong in the file.
- **Exporting a key that only compose cares about** → harmless. Make passes it to compose, which reads the same value
  from the file anyway.

## Migration Plan

Nothing to migrate. Without `compose/.env`, behaviour is unchanged. To roll back, revert the Makefile block.
