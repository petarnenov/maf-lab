# Design

## Context

See proposal.md, Why. Constraints that shape the approach:

- The CI `specs` job has Node and nothing else: no .NET SDK, no Docker. The GitHub runner does have Python 3, and the
  repository already keeps small Python helpers in `scripts/` (`corpus_stats.py`, `a2a_probe.py`).
- The Makefile must stay GNU Make 3.81 compatible, and multi-line logic lives in scripts.
- `docs/http-api.md` is mostly hand-written prose: bodies, responses, the AG-UI event contract. Only its method/path
  columns are facts the code holds.
- The api's routes are registered in `src/Maf.Lab.Api/Endpoints/*.cs`, `src/Maf.Lab.Api/Code/CodeSnippets.cs`,
  `src/Maf.Lab.A2A/A2AEndpoints.cs` and `src/Maf.Lab.Hosting/InstanceIdentity.cs`. Registrations use string literals,
  `MapGroup` prefixes held in a local variable, and `const string` fields (`AgentCardFactory.WellKnownPath`). The A2A SDK
  (`MapA2A`) and the MCP SDK (`MapMcp`) register routes that no literal in this repository shows.
- The canonical model values are code defaults:
  - chat: `ModelOptions.ChatModel` in `src/Maf.Lab.Retrieval/Configuration/Options.cs`, which must equal the Makefile's
    `CHAT_MODEL`;
  - embedding: the default profile's `Model` in the same file;
  - Jev: `JevOptions.Model` in `src/Maf.Lab.Retrieval/Jev/JevOptions.cs`.
- `openspec/config.yaml`'s `context:` is a YAML block scalar that must equal `openspec/project.md`.

## Goals / Non-Goals

**Goals:**
- Every fact that code or config holds is either generated into the docs or checked against them, with no third
  option.
- The check is deterministic, fast (under 2 s), needs no network, and gives a message that says what to do.
- Adding a doc rule later means adding one function and one test.

**Non-Goals:**
- Generating whole documents. Prose stays hand-written.
- Judging whether prose is still true. That is the advisory archive review, not the check.
- Checking the web UI's text, code comments, `DECISIONS.md`, eval reports or archived changes.
- Checking the MCP tool catalogue against docs. It is a candidate for a later rule, but out of scope here.

## Decisions

### One script, two modes

`scripts/docs.py` has two subcommands. `generate` backs `make docs`: it rewrites the generated blocks in place.
`check` backs `make docs-check`: it runs every rule, prints one line per finding
(`path:line: rule: message → fix`) and exits 1 if there are any.

`check` computes each generated block in memory and compares it with the file, so the two modes cannot disagree. The
script uses the Python standard library only, so there is no `pip install` in CI or `make setup`.

*Alternatives:*
- A .NET tool or xUnit test. It would need the SDK in the `specs` job and a build before a doc check, which is slow
  and heavy for a text check.
- Node, since the `specs` job has it. It would add a `package.json` outside `web/`. Python is already the repository's
  scripting language beside bash.
- Bash. Parsing C# route registrations and nginx blocks in bash is fragile.

### Marker syntax

In markdown files, a block sits between `<!-- generated:NAME — edit the source, then run make docs -->` and
`<!-- /generated:NAME -->`. In YAML, the markers are `# generated:NAME …` and `# /generated:NAME` comment lines at
column 0.

A column-0 comment ends a block scalar, so the YAML markers can sit directly around `context: |` and its body. That
keeps `rules:` and `operations:` outside the block. The comment text in the start marker names the fix, so a reader who
edits by hand learns why the edit will be lost.

### Sources per generated block

| Block | Where | Source |
|---|---|---|
| `make-targets` | README (replaces the hand list) | Makefile lines matching `^target: … ## text`, the same regex `make help` uses, in file order |
| `repo-layout` | `openspec/project.md`, and so `config.yaml` too | `<Description>` of each `src/*/*.csproj` and `tools/*/*.csproj`; other top-level directories from `docs/docs-sync.toml` `[layout]` |
| `lb-routes` | README, `.github/copilot-instructions.md` | `compose/lb/nginx.conf`: each `location` (exact `=` or prefix), its `proxy_pass` upstream, and the upstream's first `server` host, which is the compose service |
| `project-context` | `openspec/config.yaml` | the whole of `openspec/project.md`, indented two spaces |

The csproj `<Description>` sits next to the code it describes, and `dotnet pack` already understands it. That makes it
a better single source than a table in the TOML file.

Order matters for `repo-layout` → `project-context`. `generate` runs the blocks in dependency order
(`repo-layout` first), so one run is enough.

### Route check: static parse, fail closed

The parser reads the four source locations above and does four things:
1. It tracks `var X = <receiver>.MapGroup("<prefix>")`, prefixes included.
2. It resolves `<receiver>.Map{Get,Post,Put,Patch,Delete}("<path>" | Const.Name`.
3. It resolves `const string` fields by name across the parsed files.
4. It normalizes route parameters to `{}`.

Any `.Map{Verb}(` or `.MapGroup(` whose argument it cannot resolve is itself a finding. A new registration style
therefore fails the check instead of slipping past it.

The documented side reads every table row in `docs/http-api.md` whose first cell is a method, or a `/`-joined list of
methods. Paths come from the backticked items in the second cell, comma-separated, after the same normalization. A
row whose path ends in `…` is a family, such as the A2A HTTP+JSON binding. Families must be listed in the TOML file's
`[routes.library]` section.

`docs/docs-sync.toml` holds two lists, each entry with a mandatory `reason`:
- `[routes.undocumented]`: registered but deliberately not in `http-api.md`, for example `GET /health`, which is
  infrastructure served by every host.
- `[routes.library]`: documented but registered by an SDK, not by a literal (`POST /a2a`, the `/a2a/...` family).
- `[routes.elsewhere]`: documented but served by another host behind the same balancer (the compliance agent's
  `/compliance/...` rows, which sit under its `A2A:PathBase`). The parser scans only the api's sources, so these rows
  are listed rather than parsed.

Table cells are split on unescaped `|` only, because a row such as `` `/api/telemetry?window=15m\|1h` `` escapes the
pipe inside its path.

*Alternative:* enumerate `EndpointDataSource` in a `WebApplicationFactory<Program>` test. It is exact, but it needs the
api to boot with its options, SQLite and Qdrant settings, and the .NET SDK in the `specs` job. The fail-closed parser
covers today's patterns, and it says when it meets one it doesn't understand.

### Model-name check: known families, configured values

`docs/docs-sync.toml` `[models]` lists, per kind (chat, embedding, jev), a regex for any name of that kind:
- `jev`: `jev-\d+\.\d+\.\d+`;
- `embedding`: the known Ollama embedding families (`nomic-embed-text`, `embeddinggemma`, `mxbai-embed-large`,
  `bge-m3`, `snowflake-arctic-embed`, `all-minilm`, `qwen3-embedding`);
- `chat`: `gpt-oss:\d+b`, `qwen3:\d+b`, `llama3[.\d]*:\d+b`.

A match in a checked document must equal the configured value, or be an entry in `[models.allowed]` with a reason. The
one allowed entry today is `qwen3:4b`, the local chat fallback that `DECISIONS.md` records.

The configured values are read from the C# defaults by anchored regexes. If an anchor stops matching, that is a finding
too, so a moved default fails loudly. `CHAT_MODEL` in the Makefile is compared with the C# default, since the two must
agree.

### `make` reference check: only in code

`make <target>` is checked only inside inline code (`` `make x …` ``) and on lines of fenced code blocks that start with
`make `. Otherwise prose like "make sure" would count as a target reference. Target names are compared with every
target the Makefile defines, including ones without `##`.

### Links

Relative `[text](path)` and `![alt](path)` in checked documents are resolved against the document's directory. The
`#anchor` suffix is dropped and the file must exist. External links (`http`, `https`, `mailto`) are not fetched.

### Process rules live in `config.yaml`, enforcement in the check

`rules.proposal` and `rules.tasks` shape what an authoring agent writes. `operations.archive.guidance` asks the
archiving agent for three things:
1. Run `make docs-check` and stop on failure.
2. Launch a read-only review of the change's diff against the checked documents, whose report lists possibly stale
   sentences.
3. Fix those sentences in the change or report them.

The archive skill treats `operations.archive.guidance` as advisory. So the one deterministic enforcement is in the check:
every active change (`openspec/changes/*` except `archive`) must have `## Documentation impact` in its proposal. CI then
fails on a pull request that skipped it.

The semantic review stays advisory because a model's reading of prose is not reproducible. A gate that is sometimes
wrong gets bypassed.

### Wiring

- `Makefile`:
  - `docs:` runs `python3 scripts/docs.py generate`, and `docs-check:` runs `python3 scripts/docs.py check`;
  - a `require-python` guard in the style of `require-npm`;
  - `ci: specs docs-check …`.
- `.github/workflows/ci.yml`: the `specs` job adds a `make docs-check` step after `make specs`.
- `scripts/tests/test_docs.py`: `unittest` cases over small fixture trees, one per rule and per generator. It runs as
  part of `make docs-check`, before the real check, so CI runs it without another job.

## Risks / Trade-offs

- **The route parser misses a pattern.** The failure mode is one of two: an unresolvable call becomes a finding, or a
  new host file isn't in the scanned set. → The scanned set is every `.cs` under `src/Maf.Lab.Api`, `src/Maf.Lab.A2A` and
  `src/Maf.Lab.Hosting`, not a list of files.
- **The model families regex misses a new model family**, so a doc could name one unchecked. → Changing the model is
  already a DECISIONS.md step, and the rule in CLAUDE.md now points at `[models]` too.
- **The `Documentation impact` rule fails the in-flight `add-coverage-dashboard-and-test-agent`** when it is merged to
  `main`. → Its proposal gains the section as part of that merge, and the coverage routes gain rows in `http-api.md`.
  This is intended: it is exactly the drift the check exists for.
- **Generated blocks make README diffs noisier** when a Makefile description changes. → Accepted: that diff is the doc
  update the change needed anyway.
- **Hand edits inside a block are lost on `make docs`.** → The start marker says so, and `check` fails before the edit
  can merge.
- **"Absolute" sync is not reachable for prose.** A sentence can describe old behavior with every fact in it still valid.
  → The advisory archive review, and keeping prose from restating generated facts.

## Migration Plan

1. Land the script, the TOML file and the tests, with `docs-check` not yet in `ci`.
2. Add `<Description>` to every csproj and markers to the four documents. Run `make docs`, and fix by hand what `check`
   reports: the three routes, the model name, the compose list and the links. This is the one-off cleanup.
3. Add the `config.yaml` rules and archive guidance, then wire `docs-check` into `ci` and `ci.yml`.
4. Rollback: remove `docs-check` from `ci` and `ci.yml`. The generated blocks stay valid markdown with or without the
  script.
