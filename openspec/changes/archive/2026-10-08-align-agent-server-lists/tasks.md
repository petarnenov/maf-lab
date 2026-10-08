# Tasks

Risk tier HIGH (proposal.md): `[mech]` → Haiku, `[std]` → Sonnet, `[hard]` → Opus (docs/rules/openspec-models.md
§4–5). This change records a fix already on `main` (`e5a4296`, 2026-10-08); each task names what verifies it there.
No Jev request is added or changed, so the jev-usage §7 checklist does not apply (task 1.3 states it).

## 1. Configuration and test

- [x] 1.1 `[mech]` `src/Maf.Lab.Api/appsettings.json`: `Agent:Servers` in the compose order — portfolio, codebase
  (with its `Tools`), bulgarian-history. Verify: `python3 -c` over the file prints
  `['portfolio', 'codebase', 'bulgarian-history']` and matches `Agent__Servers__N__Domain` in
  `compose/docker-compose.yml`.
- [x] 1.2 `[std]` `tests/Maf.Lab.Tests/AgentServersConfigurationTests.cs`: (a) every `Agent__Servers__N__Domain` in
  the compose api environment equals `Agent:Servers[N].Domain` in the JSON, failing with the position and both domains;
  (b) the JSON plus the compose environment, bound to `AgentOptions` as `Program.cs` does, yields the four domains,
  the history server at `http://lb/bulgarian-history/mcp` with an empty `Tools`, an empty `Tools` on portfolio, and
  the three code tools on codebase. Verify: `dotnet test tests/Maf.Lab.Tests --filter AgentServersConfigurationTests`
  passes; with the two JSON lines swapped back it fails on both tests.
- [x] 1.3 `[mech]` No Jev request is touched: the classification request, its state, thresholds and model are
  unchanged. Verify: `git show e5a4296 --stat` lists no file under `src/Maf.Lab.Api/Agent/Jev/`.
- [x] 1.4 `[std]` The fix in the running stack: `make up` rebuilds the api; a history question through the balancer
  (`POST /api/chat` with a dev token, "Разкажи за подготовката на Априлското въстание") forces
  `search_bulgarian_history`, returns sources, and the answer cites the corpus. Verify: the AG-UI stream carries
  `TOOL_CALL_START` for `search_bulgarian_history` and a `TOOL_CALL_RESULT` with `sourceCount` > 0; the full unit
  suite (`dotnet test tests/Maf.Lab.Tests`) is green.

## 2. Documentation

- [x] 2.1 `[mech]` `DECISIONS.md` §81: the bullet "the two server lists must agree by index" — the cause, the fix, the
  test, why local runs never saw it. Verify: `grep -c "agree by index" DECISIONS.md` is 1.
- [x] 2.2 `[mech]` `make docs` (no generated block is affected) and `make docs-check`. Verify: `make docs-check`
  reports "in sync".
