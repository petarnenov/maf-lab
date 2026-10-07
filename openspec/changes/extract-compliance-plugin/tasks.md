## 1. Move `compliance` into `plugins/compliance/`

- [x] 1.1 The plugin folder: manifest, compose (the reviewer, the api's client settings), lb fragments, `plugin.mk`
      (replicas, the eval's `EVAL_ENV`), `files/compliance.env`; the agent moved to `service/` (out of `maf-lab.sln`);
      the client and the screen's routes in `server/`; the screen in `web/`; its tests and `docs/http-api.md`.
- [x] 1.2 `IAuditTrail` in Abstractions, implemented by the core (`CoreAuditTrail`); the records stay core.
- [x] 1.3 `tool_requires: compliance` by catalogue; `NoReviewer`; the client on `IWriteAudit` (actor: the request's);
      `PluginHost.InstalledServices` for the eval; `TopologyProbe` reads the raw setting.
- [x] 1.4 `Directory.Build.targets`: plugin services referenced from the test projects under extern alias `service`.
- [x] 1.5 Tests split: `ScriptedReviewer` for billing's flow; hostile verdicts read off the wire in compliance; what
      billing writes in billing.
- [ ] 1.6 After extract-portfolio lands: delete `LegacyCapabilities`, `BuiltIn/` and the scanner's exemption; replace
      the `BuiltInDomains` consts in tests with `StandInDomains`; drop `test_plugins.py`'s `BuiltIn` glob.
- [ ] 1.7 DECISIONS §81 part H.

## 2. Verify

- [ ] 2.1 `make test` with the folder present, with it moved aside, and with `plugins/billing` moved aside;
      `make docs-check` and `openspec validate --strict`.
- [ ] 2.2 `make ci-e2e` (every plugin).
