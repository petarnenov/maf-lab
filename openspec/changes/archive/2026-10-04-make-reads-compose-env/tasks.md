# Tasks

## 1. Make reads compose/.env

- [x] 1.1 Add the block after the `COMPOSE` setup in `Makefile`. It turns each `KEY=value` line of `compose/.env`
  into `KEY?=value` through `$(eval)`, with a space placeholder, then exports the keys. Verify: with
  `OLLAMA_BATCH_THREADS=4` in the file, `HOST_ENV` expands to `Models__BatchOllamaNumThread=4`.
- [x] 1.2 Verify precedence and edge cases with a probe makefile that includes `Makefile`:
  - `OLLAMA_BATCH_THREADS=7` in the environment gives 7.
  - `OLLAMA_BATCH_THREADS=9` on the command line gives 9.
  - With the file moved away, the defaults stay (12).
  - `SPACED_PROBE=a b  c` comes through exactly.
- [x] 1.3 Skip secret keys from the file: `JEV_MAF_LAB`, or a name ending in `_KEY`, `_TOKEN`, `_SECRET` or
  `_PASSWORD`. Verify with the probe: put `OLLAMA_API_KEY=probe` in `compose/.env`, run with `OLLAMA_API_KEY` unset in
  the environment, and check that make neither sets nor exports it. An `OLLAMA_*_THREADS` line still comes through.
- [x] 1.4 Run `make doctor` with the 8-CPU `compose/.env`. Verify: no `ollama-cpus` warning, because the doctor now
  sees `0-3` / `4-7`.
- [x] 1.5 Run `make` with no variables on the command line. Verify: all services are healthy and the `ollama-batch`
  container's cpuset is `4-7` (`docker inspect`).

## 2. Documentation

- [x] 2.1 README.md, Ollama CPU paragraph (outside the `generated:` blocks): say that the variables can be set once
  in `compose/.env`, which compose and make both read, and that secrets stay in the environment. Verify by reading
  the rendered paragraph.
- [x] 2.2 Run `make docs`, then `make docs-check`. Verify: "in sync", with no change to any `generated:` block.
- [x] 2.3 Run `make lint`, and `openspec validate make-reads-compose-env --strict`. Verify: both pass.
