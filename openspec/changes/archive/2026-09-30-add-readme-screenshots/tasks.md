# Tasks

## 1. Capture tool

- [x] 1.1 Create `tools/screenshots/` with `package.json` (private, ESM, `playwright` pinned exact) and a committed lockfile (`node_modules/` is already ignored repo-wide by `.gitignore`). Verify: `npm ci` in the directory succeeds and `git status` does not show `node_modules`
- [x] 1.2 Write `capture.mjs`: log in as `alice` via `/dev/token` into `sessionStorage`, set a 1440×900 viewport with scale factor 1600/1440, run each recipe (`chat`, `code-snippets`, `topology`, `jev`, `evals`, `compliance`), and capture `-light.png`/`-dark.png` by emulating the OS colour scheme. Honour `SHOTS=`, and fail with the screen's name on a timeout. Verify: `node capture.mjs` against the running stack writes 12 files, each 1600 px wide (`file docs/screenshots/*.png`)
- [x] 1.3 Add `make screenshots` (listed in `make help`): check npm, check the stack is up, install the package and Chromium when missing, then run the script. Verify: `make screenshots SHOTS=topology` re-writes only the two topology files
- [x] 1.4 Record `playwright` and its version in `DECISIONS.md`, including why it lives in `tools/screenshots/` and not `web/`. Verify: the pinned version in `package.json` matches the DECISIONS.md entry

## 2. Screenshots

- [x] 2.1 Run `make screenshots` for the full set. Verify: 12 PNGs, each pair shows the same content, and the script's size report shows each file under 400 KB (or a warning is explained)
- [x] 2.2 Check every image by eye for keys, bearer tokens, `OLLAMA_API_KEY`/`JEV_MAF_LAB` values, personal email addresses and non-seed data. Verify: none found in any file

## 3. README

- [x] 3.1 Add the hero chat `<picture>` under the introduction, and after the paragraph under the ASCII diagram a two-cell gallery of the screens no section is about (Jev, evals). Each image has dark/light sources, a light fallback and descriptive `alt`. Verify: GitHub's rendering shows the right variant in both themes
- [x] 3.2 Add the full-width image, once each, next to the topology paragraph, **Asking about the code** and **Compliance**, and a `make screenshots` row in the Quick start table. Verify: every referenced `docs/screenshots/*.png` exists (`grep -o 'docs/screenshots/[^"]*' README.md | sort -u | xargs ls`), and every captured file is referenced

## 4. Architecture diagram and README sync

- [x] 4.1 Replace the ASCII diagram with a Mermaid flowchart covering every compose service (three MCP servers, compliance, Redis, SQLite, Qdrant, Ollama, Ollama Cloud, Jev, OTel → Prometheus · Jaeger), with edges taken from `docs/topology.drawio` and `compose/lb/nginx.conf`. Verify: Mermaid 11 renders it without error in the default and dark themes, and every compose service appears
- [x] 4.2 Audit README against the code and correct each stale statement in place: replica counts and balancer routes, `make verify` check count (README and `make help`), domain eval size, Jev warm-up services, `make dev` ports, the local `ollama pull`, the CI e2e description, eval and index targets, tool paths, the VS Code compound. Add links to the change archive, `docs/*.md` and the screen list. Verify: each corrected number matches its source (`make verify` output, `wc -l evals/domain.jsonl`, compose replicas)

## 5. Verification

- [x] 5.1 Run `openspec validate add-readme-screenshots --strict` and `openspec validate --all --strict`. Verify: both pass
- [x] 5.2 Run `make lint-web` and `make test-web` to confirm `web/` is untouched by the new package. Verify: both pass
