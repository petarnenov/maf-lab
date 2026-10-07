## 1. Move `compliance` into `plugins/compliance/`

- [x] 1.1 The plugin folder: manifest, compose (the reviewer only; the api's client settings stay in the core's
      `compose/env/compliance.env`), lb fragments, `plugin.mk` (replicas, the eval's `EVAL_ENV`); the agent moved to
      `service/` (out of `maf-lab.sln`);
      the client and the screen's routes in `server/`; the screen in `web/`; its tests and `docs/http-api.md`.
- [x] 1.2 `IAuditTrail` in Abstractions, implemented by the core (`CoreAuditTrail`); the records stay core.
- [x] 1.3 `tool_requires: compliance` by catalogue; `NoReviewer`; the client on `IWriteAudit` (actor: the request's);
      `PluginHost.InstalledServices` for the eval; `TopologyProbe` reads the raw setting.
- [x] 1.4 `Directory.Build.targets`: plugin services referenced from the test projects under extern alias `service`.
- [x] 1.5 Tests split: `ScriptedReviewer` for billing's flow; hostile verdicts read off the wire in compliance; what
      billing writes in billing.
- [x] 1.6 After extract-portfolio lands: delete `LegacyCapabilities` and its doc lines only. `BuiltInDomains.cs` (the
      two name constants), `BuiltIn/`, the scanner's exemption and the tests' constants stay until their last reader
      moves: JevStatistics (insights), FeedbackEndpoints (feedback-review), TopologyProbe (topology), EvalAgentHost and
      DomainSuite (evals).
- [x] 1.7 DECISIONS §81 part H.

## 2. Verify

- [x] 2.1 `make test` with the folder present, with it moved aside, and with `plugins/billing` moved aside;
      `make docs-check` and `openspec validate --strict`. On 4be647f (on 5d6dc91), under
      `caffeinate -is`: present 1870/1871 (the one failure, `TestGenRunsApiTests` reading `verifying` before
      `candidate`, passes alone here and on the tip); compliance aside 1799/1799 and web 690/690; billing aside
      1732/1732 and web 697/697. docs-check in sync; validate --strict passes.
- [x] 2.2 `make ci-e2e` on cdb768a (the CI set, with `compliance` added): exit 0. A2A conformance 10/10, including
      c-10 reviewer-consultation; AG-UI 8/8. The run before the CI-set fix failed only c-10 (404: no reviewer).
