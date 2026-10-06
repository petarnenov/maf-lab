## 1. Move `compliance` into `plugins/compliance/`

- [ ] 1.1 Create `plugins/compliance/` with the reviewer, client, `/compliance` route and the audit screen; keep the audit records core.
- [ ] 1.2 Resolve `tool_requires: compliance` by plugin; implement the reviewer-consultation port in the plugin.
- [ ] 1.3 Delete `LegacyCapabilities`, `BuiltIn/` and the scanner's exemption.
- [ ] 1.4 Update the docs and DECISIONS §81.

## 2. Verify

- [ ] 2.1 `make test`, `make docs-check` and `openspec validate --strict` pass with the folder present, and again with
      it moved aside.
- [ ] 2.2 `make ci-e2e` (every plugin) and `make ci-e2e-core` (the core alone) pass.
