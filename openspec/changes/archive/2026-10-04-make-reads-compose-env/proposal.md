# Proposal

## Why

The Ollama CPU defaults (`0-3` / `4-15`) need 16 CPUs in Docker's VM. On a host with 8, plain `make` fails:
`ollama-batch` cannot start ("Requested CPUs are not available - requested 4-15, available: 0-7").

The documented fix is to set `OLLAMA_*_CPUS` and `OLLAMA_*_THREADS`. Compose already reads them from the
machine-local, git-ignored `compose/.env`, so a developer can put them there once. Make does not read that file. So
the host-side CLIs (`make dev`, host indexing through `HOST_ENV`) and `make doctor` keep the defaults. The batch
instance then runs on 4 CPUs, but those CLIs send it `num_thread=12`. Ollama reloads the model on every such request,
and the doctor warns about CPU sets that are not in use.

## What Changes

- **Make reads `compose/.env`.** When the file exists, make reads it at startup and exports its `KEY=value` lines. The
  targets, their scripts and the host-side CLIs then see the same values as compose. Without the file nothing changes.
- **Same precedence as compose.** A variable set in the environment or on the make command line wins over the file.
- **Values are literal.** Make takes no quotes and no `export` prefix. Spaces inside a value are kept.
- **Secrets are not read from the file.** A key that names a secret is skipped: `JEV_MAF_LAB`, or a name ending in
  `_KEY`, `_TOKEN`, `_SECRET` or `_PASSWORD`. Secrets still come only from the environment. Compose's own reading of
  the file is unchanged.
- No new target and no long-running process; nothing new needs progress feedback (progress-feedback). No Jev call.

Implemented in `Makefile`, after the `COMPOSE` setup. Verified
by hand:
- **File only:** `HOST_ENV` sends `Models__BatchOllamaNumThread=4`.
- **Environment override:** `OLLAMA_BATCH_THREADS=7` wins over the file.
- **Command-line override:** `OLLAMA_BATCH_THREADS=9` on the make command line wins over the file.
- **No file:** the defaults stay (`12`).
- **Spaced value:** `a b  c` is kept exactly.
- `make docs-check` and `make help` pass.

## Capabilities

### New Capabilities
<!-- none -->

### Modified Capabilities
- `make-workflow`: "Configuration through variables" adds the machine-local `compose/.env` as a source for make's
  variables. The environment and the command line win over it. Secrets are still read only from the environment.

## Impact

- `Makefile`: one block near the top that reads `compose/.env`.
- Everything make runs (scripts, compose, host CLIs, `make doctor`) sees the file's values.
- No change to compose files, services, images, ports or package versions.

## Documentation impact

- **README.md**, the Ollama CPU paragraph (outside any `generated:` block): add that the variables can be set once in
  `compose/.env`, which compose and make both read, and that secrets do not go there.
- No other document is affected:
  - CLAUDE.md, openspec/project.md and docs/*.md do not describe how make gets its variables.
  - No route, target, project, model or lb location changes, so no `generated:` block changes.
